using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LukeSkywalker.IPNetwork;
using System.Diagnostics;
using System.Net;
using NLog;
using Fclp;
using System.Threading;

namespace IPUrlSweep
{
    class Program
    {
        static List<IPAddress> m_sHits = new List<IPAddress>();
        static Logger _log = LogManager.GetCurrentClassLogger();
        static int _threadCount = 0;
        static int _maxThreads = 1;
        static object lockObject = new object();

        static bool DNSMatch(IPAddress addr, string Domain)
        {
            try
            {
                IPHostEntry dnsName = Dns.GetHostEntry(addr);

                if (dnsName.HostName == Domain)
                {
                    _log.Debug("Match! {0} -> {1}. Target {2}.", addr, dnsName.HostName, Domain);
                    return true;
                }

                _log.Debug("{0} -> {1}. Target {2}.", addr, dnsName.HostName, Domain);
            }
            catch (Exception exp) 
            {
                _log.Error(exp);
            }

            return false;
        }

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

        static void ProcessRange(string Range, string Pattern, string Code, string SearchString, string targetDNS)
        {
            IPNetwork network = IPNetwork.Parse(Range);
            IPAddressCollection addresses = IPNetwork.ListIPAddress(network);
            int expectedCode = Int32.Parse(Code);
            foreach (IPAddress address in addresses)
            {

                // wait until a thread is free
                while (_threadCount >= _maxThreads) { Thread.Sleep(100); }
                // Start the threads
                Task.Run(()=>ProcessAddress(Pattern, SearchString, targetDNS, expectedCode, address));
                // increase the thread count
                lock (lockObject)
                {
                    _threadCount++;
                }

                Console.CursorLeft = 0;
                Console.CursorTop = 0;
                _log.Debug("Address:    {0}", address.ToString());
                Console.Write("Address:     {0}         ", address.ToString());

                Console.CursorLeft = 0;
                Console.CursorTop = 1;
                _log.Debug("Threads:    {0}", _threadCount);
                Console.Write("Threads:     {0}         ", _threadCount);
            }
        }

        private static void ProcessAddress(string Pattern, string SearchString, string targetDNS, int expectedCode, IPAddress address)
        {
            bool targetMatch = false;

            if (targetDNS != "*")
            {
                targetMatch = DNSMatch(address, targetDNS);
            }
            else
            {
                targetMatch = true;
            }

            if (targetMatch)
            {
                string url = "http://" + address.ToString() + "/" + Pattern;
                string content = string.Empty;
                _log.Debug("DNS Match.... created url {0}", url);
                bool bHit = false;

                int resp = GetResource(url, out content);

                if (resp == expectedCode)
                {
                    // now check for search string
                    if (SearchString != "*")
                    {
                        // check to see if there are any hits in the content
                        if (content.Contains(SearchString))
                        {
                            bHit = true;
                        }
                        else
                        {
                            bHit = false;
                        }
                    }
                    else
                    {
                        bHit = true;
                    }

                    if (bHit)
                    {
                        m_sHits.Add(address);
                        Console.CursorLeft = 0;
                        Console.CursorTop = 2;
                        _log.Debug("Address: {0} Code: {1} Expected: {2}", address.ToString(), resp, expectedCode);
                        Console.Write("Address:     {0}         Code:           {1}     Expected:       {2}     **Confirmed**", address.ToString(), resp, expectedCode);
                    }
                }
                else
                {
                    _log.Debug("Address: {0} Wanted: {1} Responded with: {2}", address, expectedCode, resp);
                }
            }

            lock (lockObject)
            {
                // We are finishing, so decrease the thread count
                _threadCount--;
            }
        }

        public class AppArgs
        {
            public string AddressRange { get; set; }
            public string Pattern { get; set; }
            public string StatusCode { get; set; }
            public string SearchString { get; set; }
            public string DNSName { get; set; }
            public int Threads { get; set; }
        }

        // Usage IPUrlSweep IPRange,..,.. URLPattern HTTPStatusCode
        static void Main(string[] args)
        {
            Stopwatch watch = new Stopwatch();

            var commandLineParser = new FluentCommandLineParser<AppArgs>();

            commandLineParser.Setup(arg => arg.AddressRange)
                .As('r', "range")
                .Required();

            commandLineParser.Setup(arg => arg.Pattern)
                .As('p', "pattern")
                .Required();

            commandLineParser.Setup(arg => arg.StatusCode)
                .As('c', "code")
                .SetDefault("200");

            commandLineParser.Setup(arg => arg.SearchString)
                .As('s', "search")
                .SetDefault("*");

            commandLineParser.Setup(arg => arg.DNSName)
                .As('d', "dnsname")
                .SetDefault("*");

            commandLineParser.Setup(arg => arg.Threads)
                .As('t', "threads")
                .SetDefault(1);

            var Result = commandLineParser.Parse(args);
            if (Result.HasErrors && Result.UnMatchedOptions.Count() != 0)
            {
                _log.Error("Invalid command line.....");
                Console.WriteLine("Usage: IPUrlSweep");
                Console.WriteLine("     --r or --range IPRange,...,... ");
                Console.WriteLine("     --p or --pattern URLPattern ");
                Console.WriteLine("     --c or --code HTTPStatusCode (Optional)"); 
                Console.WriteLine("     --s or --search search content for string (Optional)");
                Console.WriteLine("     --d or --dnsname dns name match for ip address (Optional)");
                Console.WriteLine("     --t or --threads number of concurrent threads (Optional)");
                return;
            }

            string target = commandLineParser.Object.AddressRange;
            string pattern = commandLineParser.Object.Pattern;
            string code = commandLineParser.Object.StatusCode;
            string searchString = commandLineParser.Object.SearchString;
            string targetDNS = commandLineParser.Object.DNSName;
            _maxThreads = commandLineParser.Object.Threads;

            _log.Debug("Address Range: {0}", target);
            _log.Debug("URL Pattern: {0}", pattern);
            _log.Debug("Search String: {0}", searchString);
            _log.Debug("Required Response Code: {0}", code);
            _log.Debug("Target DNS: {0}", targetDNS);

            watch.Start();
            // check the IPRange portions
            // is it an array of ranges
            if (target.Contains(','))
            {
                string[] ranges = target.Split(',');

                foreach (string range in ranges)
                {
                    ProcessRange(range, pattern, code, searchString, targetDNS);
                }
            }
            else
            {
                // Single range
                ProcessRange(target, pattern, code, searchString, targetDNS);
            }

            watch.Stop();
            Console.Clear();
            Console.WriteLine("Confirmed Servers IPs.......");
            foreach (IPAddress addr in m_sHits)
            {
                _log.Info("Address: {0} -> Hit!", addr);
                Console.WriteLine("Address:   {0}   ", addr);
            }
            _log.Info("Completed in {0}", watch.Elapsed);
            Console.WriteLine("Process Completed in {0}", watch.Elapsed);
        }
    }
}
