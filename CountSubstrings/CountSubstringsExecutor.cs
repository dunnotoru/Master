using MemoryPack;
using Shared;

namespace CountSubstrings;

public class CountSubstringsExecutor : IAlgorithmExecutor
{
    public Guid Id { get; } = Guid.Parse("1402cb38-e195-4781-a708-4ec769a17d76");
    public string Name { get; } = "count-substrings";
    public Type ResultType { get; } = typeof(int);
    public string ArgsSchema { get; } = "substring=string-value";


    public byte[] Execute(IDictionary<string, byte[]> parameters)
    {
        string text = MemoryPackSerializer.Deserialize<string>(parameters["text"])!;
        string substring = MemoryPackSerializer.Deserialize<string>(parameters["substring"])!;

        Console.WriteLine("Executing {0}", Name);
        Console.WriteLine("Parameters are {0}", string.Join(", ", parameters));

        int result = FindSubstrings(text, substring);
        Console.WriteLine("Return Value is {1}", result);

        return MemoryPackSerializer.Serialize(result);
    }

    public List<Assignment> CreateAssignments(IDictionary<string, string> args, out Guid jobId)
    {
        jobId = Guid.NewGuid();
        string file = args["file"];
        string substring = args["substring"];

        List<Assignment> asses = new List<Assignment>();

        using StreamReader fs = File.OpenText(file);
        char[] buffer = new char[1024 * 64];

        for (int i = 0; !fs.EndOfStream; i++)
        {
            int read = fs.ReadBlock(buffer);
            AssignmentIdentifier id = new AssignmentIdentifier(jobId, i);
            Assignment ass = new Assignment(id, i, Name,
                new Dictionary<string, byte[]>
                {
                    ["text"] = MemoryPackSerializer.Serialize(new string(buffer, 0, read)),
                    ["substring"] = MemoryPackSerializer.Serialize(substring)
                }
            );

            asses.Add(ass);
        }

        return asses;
    }

    public object AggregateResults(List<AssignmentResult> results)
    {
        int sum = 0;
        foreach (AssignmentResult result in results)
        {
            int value = MemoryPackSerializer.Deserialize<int>(result.Result);
            sum += value;
        }

        return sum;
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