using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    class Program
    {
        private static string certName = "../../TempCA.pfx";
        private static string certPsw = "vsw1e3t56";

        static void Main(string[] args)
        {
            //string currentPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            //Console.WriteLine("current path: " + currentPath);
            Server.RunServer(certName, certPsw);
        }
    }
}
