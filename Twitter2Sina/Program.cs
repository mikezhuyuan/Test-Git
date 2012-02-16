using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Web;
using Twitterizer;
using System.Threading;

namespace ConsoleApplication3
{
    class Program
    {
        static void Main(string[] args)
        {
            var sinas = new List<Sina>{
                new RealSina("mikezhu@eikeconsulting.com", "1234qwer", "2448595899"),
                new RealSina("twitter_dummy0@mailinator.com", "1234qwer", "2546876916"),
                new RealSina("twitter_dummy1@mailinator.com", "1234qwer", "51923812"),
                new RealSina("twitter_dummy2@mailinator.com", "1234qwer", "2343725106"),
            };
            
            Engine engine = new RealEngine(sinas);
            engine.Process();
        }

        class RealEngine : Engine
        {
            public RealEngine(IEnumerable<Sina> dummies) : base(dummies) { }

            public override IEnumerable<TwitterStatus> GetTweets(int count, decimal lastStatusID)
            {
                var statuses = Helper.GetTweets(count, lastStatusID);
                Console.WriteLine("GetTweets: " + statuses.Count());
                return statuses;
            }
        }

        class RealSina : Sina
        {
            public RealSina(string username, string password, string appkey)
                : base(username, password, appkey)
            { }

            protected override void DoPost(string username, string password, string appkey, string contents)
            {
                Helper.PostOnSina(username, password, appkey, contents);
                Console.WriteLine("Posted:" + username + " " + contents);
            }
        }

        class DummyEngine : Engine
        {
            public DummyEngine(IEnumerable<Sina> dummies) : base(dummies) { }
            static int id = 0;
            public override IEnumerable<TwitterStatus> GetTweets(int count, decimal lastStatusID)
            {
                var result = new List<TwitterStatus>();
                for (int i = 0; i < 41; i++)
                {
                    result.Add(new TwitterStatus
                    {
                        Id = id++,
                        CreatedDate = DateTime.Now.AddMinutes(id),
                        Text = "Tweet" + id,
                        User = new TwitterUser
                        {
                            Location = "Location" + id
                        }
                    });
                }
                return result;
            }
        }

        class DummySina : Sina
        {
            public DummySina(string username, string password, string appkey)
                : base(username, password, appkey)
            { }
            protected override void DoPost(string username, string password, string appkey, string contents)
            {
                Console.WriteLine(username + ":" + contents);
            }
        }
    }

    abstract public class Engine
    {
        int tweetOnceCount = 20;
        decimal lastStatusID;
        int dummyNo = 0;
        int sina_interval = 0;//200;
        int tweet_interval = 1000;//20 * 60 * 1000;
        List<TwitterStatus> tweetsQueue = new List<TwitterStatus>();
        List<Sina> sinaDummies;
        abstract public IEnumerable<TwitterStatus> GetTweets(int count, decimal lastStatusID);

        public Engine(IEnumerable<Sina> dummies)
        {
            if (dummies == null || dummies.Count() == 0)
                throw new ArgumentException();

            sinaDummies = new List<Sina>(dummies);
            var str = File.ReadAllText("LastTwitterStatusID.txt");
            decimal.TryParse(str, out lastStatusID);
        }

        public void Process()
        {
            while (true)
            {
                if (tweetsQueue.Count == 0)
                {
                    try
                    {
                        var tweets = GetTweets(tweetOnceCount, lastStatusID);
                        tweetsQueue.AddRange(tweets);
                        tweetsQueue = tweetsQueue.OrderBy(_ => _.Id).ToList();
                    }
                    catch (Exception ex)
                    {
                        //log error
                        Console.WriteLine("Failed: GetTweets");
                    }
                }

                if (tweetsQueue.Count == 0 || sinaDummies.All(_ => _.QuotaLeft <= 0))
                    Thread.Sleep(tweet_interval);

                while (tweetsQueue.Count > 0)
                {
                    var status = tweetsQueue[0];
                    var contents = ComposeContent(status);
                    try
                    {
                        var processed = sinaDummies[dummyNo].PostOnSina(contents);
                        dummyNo = (dummyNo + 1) % sinaDummies.Count;

                        if (processed)
                        {
                            tweetsQueue.RemoveAt(0);
                            if (status.Id > lastStatusID)
                            {
                                lastStatusID = status.Id;
                                File.WriteAllText("LastTwitterStatusID.txt", lastStatusID.ToString());
                            }
                        }

                        Thread.Sleep(sina_interval);
                    }
                    catch (Exception ex)
                    {
                        //log error
                        Console.WriteLine("Failed: " + status.Id + " " + status.Text);
                    }
                }
            }
        }

        string ComposeContent(TwitterStatus tweet)
        {
            return string.Format(
   @"{0}:\n{1}\n{2} {3}",
   tweet.User.Name,
   tweet.Text.Length > 80 ? tweet.Text.Substring(0, 80) : tweet.Text,
   tweet.User.Location,
   tweet.CreatedDate
   );
        }
    }

    abstract public class Sina
    {
        public const int QUOTA = 10;
        public int QuotaLeft { get; private set; }
        public DateTime LastQuotaBegin { get; private set; }
        int error = 0;

        string username;
        string password;
        string appkey;

        public Sina(string username, string password, string appkey)
        {
            this.username = username;
            this.password = password;
            this.appkey = appkey;
            QuotaLeft = QUOTA;
        }

        public bool PostOnSina(string contents)
        {
            if (QuotaLeft <= 0)
            {
                if (DateTime.Now < LastQuotaBegin.AddHours(1))
                    return false;
                QuotaLeft = QUOTA;
                LastQuotaBegin = DateTime.Now;
            }

            try
            {
                DoPost(username, password, appkey, contents);
            }
            catch (Exception ex)
            {
                error++;
                if (error >= 3)
                {
                    error = 0;
                    QuotaLeft = 0;
                }                
                throw ex;
            }
            QuotaLeft--;
            return true;
        }

        abstract protected void DoPost(string username, string password, string appkey, string contents);
    }

    public class Helper
    {
        public static string PostOnSina(string username, string password, string appkey, string contents)
        {
            string url = "http://api.t.sina.com.cn/statuses/update.xml";
            var request = WebRequest.Create(url) as HttpWebRequest;

            request.Credentials = new NetworkCredential(username, password);
            byte[] authBytes = Encoding.UTF8.GetBytes(username + ":" + password);
            request.Headers["Authorization"] = "Basic " + Convert.ToBase64String(authBytes);
            request.Method = "POST";

            request.ContentType = "application/x-www-form-urlencoded";            
            var body = "source=" + HttpUtility.UrlEncode(appkey) + "&status=" + HttpUtility.UrlEncode(contents);

            using (var writer = new StreamWriter(request.GetRequestStream()))
            {
                writer.Write(body);
            }

            WebResponse response = request.GetResponse();
            using (Stream receiveStream = response.GetResponseStream())
            {
                StreamReader reader = new StreamReader(receiveStream, Encoding.UTF8);
                string result = reader.ReadToEnd();
                return result;
            }
        }

        public static IEnumerable<TwitterStatus> GetTweets(int count, decimal lastStatusID)
        {
            OAuthTokens tokens = new OAuthTokens();
            tokens.AccessToken = "60750520-5y86rngWiV0HHeGypCXkgsMCfaY6XNfQfJdrCNQ2p";
            tokens.AccessTokenSecret = "AkHCV5ERKsk3DbK8rJo0vjEbM54U3iPKSkWAjwZjo";
            tokens.ConsumerKey = "zjhazoD5Semfyy9PAEYNCw";
            tokens.ConsumerSecret = "yROVlz3Z2kO3d2GChTfXdX7D3B668hZbRuHkCtlY";

            var response = Twitterizer.TwitterTimeline.HomeTimeline(tokens, new TimelineOptions { Count = count, SinceStatusId = lastStatusID });
            if (response.Result == RequestResult.Success)
            {
                return response.ResponseObject;
            }
            return new List<TwitterStatus>();
        }
    }
}
