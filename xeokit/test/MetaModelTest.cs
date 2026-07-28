using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using XeokitMetadata;

namespace test {
  public class MetaModelTest {

    private string ifcPath;
    private string mock;

    [SetUp]
    public void setup() {
      ifcPath = @"resources/unitTestCase.ifc";

      using (var r = new StreamReader(@"resources/metaModelMock.json")) {
        mock = r.ReadToEnd();
      }
    }

    [Test]
    public void metaModelTest() {
      try {
        var metaModel = MetaModel.fromIfc(ifcPath);
        var json = metaModel.serialize();

        var actual = JObject.Parse(NormalizeJsonNumbers(json));
        var expected = JObject.Parse(NormalizeJsonNumbers(mock));
        Assert.True(JToken.DeepEquals(actual, expected),
          $"Data is not equal with required one.\nActual:   {json}\nExpected: {mock}");
      }
      catch (Exception e) {
        Console.WriteLine(e);
        Assert.True(false, e.Message);
      }
    }

    /// <summary>
    /// Normalizes locale-dependent number strings in JSON so that
    /// "3,48666666666667" and "3.48666666666667" parse to the same double.
    /// Handles both comma-as-decimal and period-as-decimal formats.
    /// </summary>
    private static string NormalizeJsonNumbers(string json) {
      // Match JSON string values that look like locale-dependent numbers:
      // "1,234" (comma decimal) or "1.234" (period decimal)
      // Pattern: opening quote, optional minus, digits, one separator + more digits, closing quote
      return Regex.Replace(json, @"""(-?\d+[,.]\d+)""", m => {
        var raw = m.Groups[1].Value;
        // Normalize comma to period for parsing
        var normalized = raw.Replace(',', '.');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) {
          return d.ToString(CultureInfo.InvariantCulture);
        }
        return m.Value;
      });
    }
  }
}