using System;
using System.IO;
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

        var actual = JObject.Parse(json);
        var expected = JObject.Parse(mock);
        Assert.True(JToken.DeepEquals(actual, expected),
          $"Data is not equal with required one.\nActual:   {json}\nExpected: {mock}");
      }
      catch (Exception e) {
        Console.WriteLine(e);
        Assert.True(false, e.Message);
      }
    }
  }
}