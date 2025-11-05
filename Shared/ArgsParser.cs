namespace Shared;

public static class ArgsParser
{
    //Schema: arg0=value0 arg1=another-value
    public static Dictionary<string, string> Parse(string value)
    {
        var dict = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(value)) return dict;

        var args = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var arg in args)
        {
            var parts = arg.Split('=', 2);
            if (parts.Length != 2)
                throw new ArgumentException("Invalid argument (too long)");

            var key = parts[0].Trim();
            var val = parts[1].Trim();

            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Empty key");

            dict[key] = val;
        }

        return dict;
    }
}