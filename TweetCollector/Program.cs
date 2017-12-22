using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CoreTweet;
using CoreTweet.Streaming;
using Newtonsoft.Json;

namespace TweetCollector
{
    internal static class Program
    {
        public static Tokens Tokens { get; private set; }
        public static WebClient WebClient => new WebClient();
        public static readonly SavedTweetList NoMediaTweetList = new SavedTweetList();
        public static readonly SavedTweetList MediaTweetList = new SavedTweetList();
        public static readonly ConcurrentQueue<Status> FailedFavorite = new ConcurrentQueue<Status>();
        public static readonly ConcurrentQueue<Status> FailedRetweet = new ConcurrentQueue<Status>();

        public static void Main(string[] args)
        {
            const string query = "#結月ゆかり誕生祭2017";
            Tokens = GetTokens();
            var stream = Tokens.Streaming.FilterAsObservable(track: query);
            var disposable = stream.Subscribe(new StreamObserver());
            try
            {
                while (true)
                {
                    Task.Delay(TimeSpan.FromHours(1));
                    Save();

                    while (true)
                    {
                        if (!FailedFavorite.TryDequeue(out var status)) continue;
                        try
                        {
                            Tokens.Favorites.Create(status);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Rate Limit Fav");
                            FailedFavorite.Enqueue(status);
                            break;
                        }
                    }
                    while (true)
                    {
                        if (!FailedRetweet.TryDequeue(out var status)) continue;
                        try
                        {
                            Tokens.Statuses.Retweet(status);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Rate Limit RT");
                            FailedRetweet.Enqueue(status);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                disposable.Dispose();
                Save();
            }
        }

        private static void Save()
        {
            var noMediaJson = JsonConvert.SerializeObject(NoMediaTweetList);
            var mediaJson = JsonConvert.SerializeObject(MediaTweetList);
            using (var writer = File.CreateText(@"D:\結月ゆかり誕生祭2017\tweet.json"))
            {
                writer.Write(noMediaJson);
            }
            using (var writer = File.CreateText(@"D:\結月ゆかり誕生祭2017\media.json"))
            {
                writer.Write(mediaJson);
            }
        }

        private static Tokens GetTokens()
        {
            //Twitter for iPad
            var session = OAuth.Authorize("CjulERsDeqhhjSme66ECg", "IQWdVyqFxghAtURHGeGiWAsmCAGmdW3WmbEx6Hck");
            Process.Start(session.AuthorizeUri.AbsoluteUri);
            Console.WriteLine("PIN入力");
            var pin = Console.ReadLine();
            return session.GetTokens(pin);
        }
    }

    internal class StreamObserver : IObserver<StreamingMessage>
    {
        public void OnNext(StreamingMessage value)
        {
            var status = (value as StatusMessage)?.Status;
            if (status == null)
            {
                return;
            }
            if (status.Entities.Media == null)
            {
                Program.NoMediaTweetList.SavedTweets.Enqueue(new SavedTweet {Id = status.Id, Text = status.FullText});
                return;
            }

            if (status.IsFavorited?.Equals(true) ?? false)
            {
                try
                {
                    Program.Tokens.Favorites.Create(status.Id);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Rate Limit Fav");
                    Program.FailedFavorite.Enqueue(status);
                }
            }
            else
            {
                return;
            }
            if (status.IsRetweeted?.Equals(true) ?? false)
            {
                try
                {
                    Program.Tokens.Statuses.Retweet(status.Id);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Rate Limit RT");
                    Program.FailedRetweet.Enqueue(status);
                }
            }
            else
            {
                return;
            }
            foreach (var mediaEntity in status.Entities.Media)
            {
                var name = mediaEntity.Id;
                var url = mediaEntity.MediaUrl;
                Program.WebClient.DownloadFile(url, @"D:\結月ゆかり誕生祭2017\" + name + '.' + url.Split('.').Last());
            }
            Program.MediaTweetList.SavedTweets.Enqueue(new SavedMediaTweet
            {
                Id = status.Id,
                Text = status.FullText,
                MediaEntities = status.Entities.Media
            });
        }

        public void OnError(Exception error)
        {
            Console.WriteLine(error);
        }

        public void OnCompleted()
        {
            //Do Nothing
        }
    }

    [JsonObject]
    internal class SavedTweetList
    {
        [JsonProperty("date")] public string Date = DateTime.Today.ToString("yyyy/mm/dd");

        [JsonProperty("size")]
        public int Size => SavedTweets.Count;

        [JsonProperty("tweets")] public ConcurrentQueue<SavedTweet> SavedTweets = new ConcurrentQueue<SavedTweet>();
    }

    [JsonObject]
    internal class SavedTweet
    {
        [JsonProperty("id")] public long Id;
        [JsonProperty("text")] public string Text;
    }

    [JsonObject]
    internal class SavedMediaTweet : SavedTweet
    {
        [JsonProperty("media")] public MediaEntity[] MediaEntities;
    }
}