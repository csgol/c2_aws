using System;
using System.Net;
using System.IO;

namespace c2_aws
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                WebRequest req = WebRequest.Create("http://3.134.82.51");
                req.Proxy = WebRequest.DefaultWebProxy;
                req.Proxy.Credentials = CredentialCache.DefaultCredentials;

                WebResponse res = req.GetResponse();
                StreamReader reader = new StreamReader(res.GetResponseStream());
                Console.WriteLine(reader.ReadToEnd());

                reader.Close();
                res.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}