using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Globalization;

namespace TurizmOperasyonSistemi.Services
{
    public class ExchangeRateResult
    {
        public string UsdBuy { get; set; }
        public string UsdSell { get; set; }
        public string EurBuy { get; set; }
        public string EurSell { get; set; }
        public string GbpBuy { get; set; }
        public string GbpSell { get; set; }
        public string SarBuy { get; set; }
        public string SarSell { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class ExchangeRateService
    {
        private const string TcmbTodayUrl = "https://www.tcmb.gov.tr/kurlar/today.xml";

        public async Task<ExchangeRateResult> GetLatestRatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var xml = await client.GetStringAsync(TcmbTodayUrl);
                var doc = XDocument.Parse(xml);
                var root = doc.Root;
                var result = new ExchangeRateResult();

                result.LastUpdated = DateTime.Now;

                string GetRate(string code, string elementName)
                {
                    try
                    {
                        var node = root.Element("Currency")?.Name; // noop to satisfy analyzer
                        var cur = root.Elements("Currency");
                        foreach (var c in cur)
                        {
                            var kod = (string)c.Attribute("Kod") ?? string.Empty;
                            if (string.Equals(kod, code, StringComparison.OrdinalIgnoreCase))
                            {
                                var el = c.Element(elementName);
                                if (el != null)
                                {
                                    var txt = el.Value.Trim();
                                    if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                                        return d.ToString("N4", CultureInfo.InvariantCulture);
                                    return txt;
                                }
                            }
                        }
                    }
                    catch { }
                    return "-";
                }

                result.UsdBuy = GetRate("USD", "ForexBuying");
                result.UsdSell = GetRate("USD", "ForexSelling");
                result.EurBuy = GetRate("EUR", "ForexBuying");
                result.EurSell = GetRate("EUR", "ForexSelling");
                result.GbpBuy = GetRate("GBP", "ForexBuying");
                result.GbpSell = GetRate("GBP", "ForexSelling");
                result.SarBuy = GetRate("SAR", "ForexBuying");
                result.SarSell = GetRate("SAR", "ForexSelling");

                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
