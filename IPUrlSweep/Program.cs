using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LukeSkywalker.IPNetwork;
using System.Diagnostics;
using System.Net;
using NLog;

namespace IPUrlSweep
{
    class Program
    {
        static List<IPAddress> m_sHits = new List<IPAddress>();
        static Logger _log = LogManager.GetCurrentClassLogger();

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

        static int GetResource(string Url)
        {
            int code = 0;

            try
            {
                WebClient cli = new WebClient();

                string Content = cli.DownloadString(new Uri(Url));

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
            }

            return code;
        }

        static void ProcessRange(string Range, string Pattern, string Code, string targetDNS)
        {
            IPNetwork network = IPNetwork.Parse(Range);
            IPAddressCollection addresses = IPNetwork.ListIPAddress(network);
            int expectedCode = Int32.Parse(Code);
            foreach (IPAddress address in addresses)
            {
                Console.CursorLeft = 0;
                Console.CursorTop = 0;
                _log.Debug("Address: {0}", address.ToString());
                Console.Write("Address:     {0}         ", address.ToString());

                bool targetMatch = false;

                if (targetDNS != string.Empty)
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

                    _log.Debug("DNS Match.... created url {0}", url);

                    int resp = GetResource(url);

                    if (resp == expectedCode)
                    {
                        m_sHits.Add(address);
                        Console.CursorLeft = 0;
                        Console.CursorTop = 1;
                        _log.Debug("Address: {0} Code: {1} Expected: {2}", address.ToString(), resp, expectedCode);
                        Console.Write("Address:     {0}         Code:           {1}     Expected:       {2}     **Confirmed**", address.ToString(), resp, expectedCode);
                    }
                    else
                    {
                        _log.Debug("Address: {0} Wanted: {1} Responded with: {2}", address, expectedCode, resp);
                    }
                }
            }
        }

        // Usage IPUrlSweep IPRange,..,.. URLPattern HTTPStatusCode
        static void Main(string[] args)
        {
            Stopwatch watch = new Stopwatch();
            if (args.Length < 3)
            {
                _log.Error("Invalid command line.....");
                Console.WriteLine("Usage: IPUrlSweep IPRange,...,... URLPattern HTTPStatusCode");
                return;
            }

            string target = args[0];
            string pattern = args[1];
            string code = args[2];
            string targetDNS = string.Empty;
            if (args.Length == 4)
            {
                
                targetDNS = args[3];
            }

            _log.Debug("Address Range: {0}", target);
            _log.Debug("URL Pattern: {0}", pattern);
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
                    ProcessRange(range, pattern, code, targetDNS);
                }
            }
            else
            {
                // Single range
                ProcessRange(target, pattern, code, targetDNS);
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
