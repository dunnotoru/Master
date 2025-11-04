using MemoryPack;
using Shared;

namespace CountSubstrings;

public class CountSubstringsExecutor : IAlgorithmExecutor
{
    public Guid Id { get; } = Guid.Parse("1402cb38-e195-4781-a708-4ec769a17d76");
    public string Name { get; } = "count-substrings";

    public byte[] Execute(IDictionary<string, byte[]> parameters)
    {
        string text = MemoryPackSerializer.Deserialize<string>(parameters["text"])!;
        string substring = MemoryPackSerializer.Deserialize<string>(parameters["substring"])!;
        
        Console.WriteLine("Executing {0}", Name);
        Console.WriteLine("Parameters are {0}", string.Join(", ", parameters));
        Console.WriteLine("Return Value is (int)1");

        return MemoryPackSerializer.Serialize(1);
        // int result = FindSubstrings(text, substring);
        // return result;
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