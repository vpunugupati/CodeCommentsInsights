using CsvHelper;
using Microsoft.Extensions.Configuration;
using Octokit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GitHubRepositoryCommentsDownLoader
{
    public class CodeComment
    {
        public long Id { get; set; }
        public long Number { get; set; }
        public string Category { get; set; }
        public string Comment { get; set; }

    }
    class Program
    {
        static readonly List<string> regExPatterns = new List<string>
                                            {
                                                {@"(@)\w+"}, //@UserName 
                                                {@"\r?\n|\r"}, //Return,EndOfLine
                                                {@"\`\`\`([\S\s]*?)\`\`\`"}, //Code Snippets - Multi line
                                                {@"^\`([\S\s]*?)\`"}, //Code Snipet - Single line                                                
                                                {@"\<([^)]+)\>"}, // Text in angular brackets
                                                {@"^\>"}, // > as start character
                                                {@"^\!\[(image)\]"}, //Image
                                                {@"(http|ftp|https)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?"}, //Url
                                                {@"\((http|ftp|https)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?\)"} //Url in parenthesis
                                            };
        static string RegexMultiplePatternReplacement(string input)
        {
            var regex = new Regex(String.Join("|", regExPatterns), RegexOptions.Multiline);
            return string.IsNullOrEmpty(input) ? "" : regex.Replace(input, "").Trim();
        }
        static async System.Threading.Tasks.Task Main(string[] args)
        {
            try
            {
                var configuration = new ConfigurationBuilder().SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName).AddJsonFile("appsettings.json", false).Build();
                var codeComments = new List<CodeComment>();
                var stateFilter = ItemStateFilter.All;
                var gitHubClient = new GitHubClient(new ProductHeaderValue(configuration.GetSection("GitHubDetails").GetSection("ProductHeader").Value))
                {
                    Credentials = new Credentials(configuration.GetSection("GitHubDetails").GetSection("GitHubAccessToken").Value)
                };
                var repositories = await gitHubClient.Repository.GetAllForOrg(configuration.GetSection("GitHubDetails").GetSection("Organization").Value);
                var repositryId = repositories.Where(rep => rep.Name.Equals(configuration.GetSection("GitHubDetails").GetSection("Repository").Value)).FirstOrDefault().Id;
                var issues = await gitHubClient.Issue.GetAllForRepository(repositryId, new RepositoryIssueRequest() { State = stateFilter });
                foreach (Issue issue in issues)
                {
                    var issueComments = await gitHubClient.Issue.Comment.GetAllForIssue(repositryId, issue.Number);
                    var userIssueComments = issueComments.Where(prc => prc.User.Type.Value.ToString().Equals("User"));
                    foreach (IssueComment issueComment in userIssueComments)
                    {
                        var comment = RegexMultiplePatternReplacement(issueComment.Body);
                        if (!string.IsNullOrEmpty(comment))
                            codeComments.Add(new CodeComment { Id = issueComment.Id, Number = issue.Number, Category = "Issue", Comment = comment });
                    }
                    Console.WriteLine("Done! for Issue Number - " + issue.Number);
                }

                var repoPullRequests = await gitHubClient.Repository.PullRequest.GetAllForRepository(repositryId, new PullRequestRequest { State = stateFilter });
                foreach (PullRequest pullRequest in repoPullRequests)
                {
                    if (pullRequest.User.Type.Value.ToString() == "User")
                    {
                        var comment = RegexMultiplePatternReplacement(pullRequest.Body);
                        if (!string.IsNullOrEmpty(comment))
                            codeComments.Add(new CodeComment { Id = pullRequest.Id, Number = pullRequest.Number, Category = "PullRequest", Comment = comment });
                    }
                    var pullRequestReviews = await gitHubClient.Repository.PullRequest.Review.GetAll(repositryId, pullRequest.Number);
                    foreach (PullRequestReview pullRequestReview in pullRequestReviews)
                    {
                        var comment = RegexMultiplePatternReplacement(pullRequestReview.Body);
                        if (!string.IsNullOrEmpty(comment))
                            codeComments.Add(new CodeComment { Id = pullRequestReview.Id, Number = pullRequest.Number, Category = "PullRequestReview", Comment = comment });
                    }
                    var pullRequestReviewComments = await gitHubClient.Repository.PullRequest.ReviewComment.GetAll(repositryId, pullRequest.Number);
                    foreach (PullRequestReviewComment pullRequestReviewComment in pullRequestReviewComments)
                    {
                        var comment = RegexMultiplePatternReplacement(pullRequestReviewComment.Body);
                        if (!string.IsNullOrEmpty(comment))
                            codeComments.Add(new CodeComment { Id = pullRequestReviewComment.Id, Number = pullRequest.Number, Category = "PullRequestReviewComment", Comment = comment });
                    }

                    Console.WriteLine("Done! for Pull Request Number - " + pullRequest.Number);
                }

                using var writer = new StreamWriter("GithubCodeComments.csv");
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                csv.WriteRecords(codeComments);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            Console.WriteLine("Done!");
        }
    }
}
