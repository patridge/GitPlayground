using Nito.AsyncEx;
using Octokit;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitPlayground
{
    class Program
    {
        static string _GitHubPersonalAccessToken;
        static string GitHubPersonalAccessToken
        {
            get {
                const string configKey = "gitHubPersonalAccessToken";
                return _GitHubPersonalAccessToken ?? (_GitHubPersonalAccessToken = ConfigurationManager.AppSettings.Get(configKey));
            }
        }
        static void Main(string[] args)
        {
            AsyncContext.Run(() => MainAsync(args));
            Console.Read();
        }

        static async void MainAsync(string[] args)
        {
            var client = new GitHubClient(new ProductHeaderValue("submodule-helper"));
            var tokenAuth = new Credentials(GitHubPersonalAccessToken);
            client.Credentials = tokenAuth;

            var user = await client.User.Current();

            Console.WriteLine($"{user.Name}");
        }
    }
}
