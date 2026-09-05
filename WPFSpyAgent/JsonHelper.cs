using System;

namespace WpfSpyAgent
{
    internal static class JsonHelper
    {
        public static string Serialize(object obj)
        {
#if NET461
            return Newtonsoft.Json.JsonConvert.SerializeObject(obj);
#else
            return System.Text.Json.JsonSerializer.Serialize(obj);
#endif
        }

        public static T? Deserialize<T>(string json)
        {
#if NET461
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
#else
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
#endif
        }
    }
}
