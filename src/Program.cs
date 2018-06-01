using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using YamlDotNet.Serialization;

namespace Haack
{
    class Program
    {
        // Valid characters when mapping from the blog post slug to a file path
        // Needs to match the regex in https://github.com/damieng/jekyll-blog-comments-azure/blob/master/JekyllBlogCommentsAzure/PostCommentToPullRequestFunction.cs
        static readonly Regex validPathChars = new Regex(@"[^a-zA-Z0-9-]");
        static readonly XNamespace ns = "http://disqus.com";
        static readonly XNamespace dsq = "http://disqus.com/disqus-internals";

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("The first argument should be the path to the disqus export file. The second argument should be the path to your Jekyll directory.");
                return;
            }
            var path = args[0];
            if (!File.Exists(path))
            {
                Console.WriteLine($"The file '${path}' does not exist.");
                return;
            }
            var jekyllPath = args[1];
            if (!Directory.Exists(jekyllPath))
            {
                Console.WriteLine($"The directory '${jekyllPath}' does not exist.");
                return;
            }
            var commentHostname = args[2]; //The host for which we want to export comments such as haacked.com.

            var stopWatch = new Stopwatch();
            stopWatch.Start();
            WriteJekyllIncludes(jekyllPath).Wait();
            Console.WriteLine($"Importing from file '${path}'.");

            var postLookup = EnumerateThreads(EnumerateElements(path), commentHostname).ToDictionary(p => p.Item1, p => p.Item2);
            var comments = EnumerateComments(EnumerateElements(path), postLookup);

            var serializer = new SerializerBuilder().Build();
            int commentCount = 0;
            foreach (var comment in comments)
            {
                WriteComment(comment, jekyllPath, serializer);
                commentCount++;
            }
            Console.WriteLine();
            Console.WriteLine($"Wrote {commentCount:N0} comments for {postLookup.Count:N0} posts in {stopWatch.Elapsed.TotalSeconds} seconds.");
        }

        static async Task WriteJekyllIncludes(string jekyllDirectory)
        {
            var httpClient = new HttpClient();
            var jekyllIncludesDirectory = Path.Combine(jekyllDirectory, "_includes");
            Directory.CreateDirectory(jekyllIncludesDirectory);
            Console.WriteLine($"Writing Jekyll Includes to '{jekyllIncludesDirectory}'. You may want to modify these to fit your site's design.");

            async Task WriteInclude(string fileName)
            {
                File.WriteAllText(
                    Path.Combine(jekyllIncludesDirectory, fileName),
                    await httpClient.GetStringAsync($"https://raw.githubusercontent.com/damieng/jekyll-blog-comments/master/jekyll/_includes/{fileName}"));
            }

            await WriteInclude("comment-new.html");
            await WriteInclude("comment.html");
            await WriteInclude("comments.html");
        }

        static void WriteComment(CommentInfo commentInfo, string jekyllDirectory, Serializer yamlSerializer)
        {
            int currentCursorTop = Console.CursorTop;
            var destinationDirectory = Path.Combine(jekyllDirectory, "_data/comments/", commentInfo.ThreadSlug);
            Directory.CreateDirectory(destinationDirectory);
            var destinationPath = Path.Combine(destinationDirectory, commentInfo.Comment.id + ".yml");
            string consoleMessage = $"Writing {destinationPath}.";
            Console.Write(consoleMessage);
            using (var writer = new StreamWriter(destinationPath))
                yamlSerializer.Serialize(writer, commentInfo.Comment);
            Console.SetCursorPosition(0, currentCursorTop);
            Console.Write(new string(' ', consoleMessage.Length));
            Console.SetCursorPosition(0, currentCursorTop);
        }

        static IEnumerable<CommentInfo> EnumerateComments(IEnumerable<XElement> elements, Dictionary<string, string> threadLookup)
        {
            foreach (var element in elements.Where(e => e.Name.LocalName.Equals("post", StringComparison.Ordinal)))
            {
                var postId = element.Element(ns + "thread").Attribute(dsq + "id").Value;
                if (threadLookup.ContainsKey(postId))
                {
                    yield return CommentInfo.Create(element, postId, threadLookup[postId]);
                }
            }
        }

        static IEnumerable<(string, string)> EnumerateThreads(IEnumerable<XElement> elements, string commentHostname)
        {
            string ParseSlug(string url)
            {
                var urlWithoutTrailingSlash = url.EndsWith('/') ? url.Substring(0, url.Length - 1) : url;
                return validPathChars
                    .Replace(urlWithoutTrailingSlash.Substring(urlWithoutTrailingSlash.LastIndexOf('/') + 1), "-")
                    .ToLowerInvariant();
            }

            foreach (var element in elements.Where(e => e.Name.LocalName.Equals("thread", StringComparison.Ordinal)))
            {
                var id = element.Attribute(dsq + "id");
                var link = element.Element(ns + "link").Value;

                if (Uri.TryCreate(link, UriKind.Absolute, out var linkUri) && linkUri.Host.Equals(commentHostname, StringComparison.OrdinalIgnoreCase))
                {
                    var slug = ParseSlug(link);
                    if (string.IsNullOrEmpty(slug))
                        Debugger.Break();
                    yield return (id.Value, slug);
                }
            }
        }

        static IEnumerable<XElement> EnumerateElements(string path)
        {
            using (var reader = XmlReader.Create(path))
            {
                reader.MoveToContent();
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        // This next bit seems wonky, but the reason we do this 
                        if (reader.Name.Equals("thread", StringComparison.Ordinal) || reader.Name.Equals("post", StringComparison.Ordinal))
                        {
                            var element = XElement.ReadFrom(reader) as XElement;
                            if (element != null)
                                yield return element;
                        }
                    }
                }
            }
        }

        private class Comment
        {
            public Comment(string id, string message, string author, string email, DateTime date)
            {
                this.id = id;
                this.message = message;
                this.author = author;
                this.email = email;
                this.date = date;
                this.gravatar = EncodeGravatar(email);
            }

            public string id { get; }
            public DateTime date { get; }
            public string author { get; }
            public string email { get; }
            public string gravatar { get; }
            public string message { get; }

            static string EncodeGravatar(string email)
            {
                if (email == null) return null;

                using (var md5 = MD5.Create())
                    return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(email))).Replace("-", "").ToLower();
            }
        }

        class CommentInfo
        {
            public static CommentInfo Create(XElement postElement, string postId, string slug)
            {
                var author = postElement.Element(ns + "author");
                var email = author.Element(ns + "email");
                var date = DateTime.Parse(postElement.Element(ns + "createdAt").Value);

                var comment = new Comment("dsq-" + postElement.Attribute(dsq + "id").Value,
                    postElement.Element(ns + "message").Value,
                    author.Element(ns + "name").Value,
                    email?.Value,
                    date);
                return new CommentInfo(slug, comment);
            }

            public CommentInfo(string slug, Comment comment)
            {
                ThreadSlug = slug;
                Comment = comment;
            }

            public Comment Comment { get; }
            public string ThreadSlug { get; }
        }
    }
}
