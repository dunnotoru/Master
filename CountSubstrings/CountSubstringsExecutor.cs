using Shared;

namespace CountSubstrings;

public class CountSubstringsExecutor : IAlgorithmExecutor
{
    public Guid Id { get; } = Guid.Parse("1402cb38-e195-4781-a708-4ec769a17d76");
    public string Name { get; } = "count-substrings";

    public object? Execute(IDictionary<string, object?> parameters)
    {
        string text = (string)parameters["text"]!;
        string substring = (string)parameters["substring"]!;

        int result = FindSubstrings(text, substring);
        return result;
    }
    
    private int FindSubstrings(string text, string substring)
    {
        int result = 0;
        int n = text.Length;
        int m = substring.Length;
        if (m == 0) return result;

        for (int i = 0; i <= n - m; i++)
        {
            int j = 0;
            for (; j < m; j++)
            {
                if (text[i + j] != substring[j]) break;
            }

            if (j == m)
            {
                result += 1;
            }
        }

        return result;
    }
}