using System;
using Velopack;

namespace TurizmOperasyonSistemi
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            VelopackApp.Build().Run();

            var app = new App();
            app.Run();
        }
    }
}
