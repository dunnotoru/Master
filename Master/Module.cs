using System.IO;
using Shared;

namespace Master;

public record Module(IAlgorithmExecutor Executor, FileInfo ModuleFile);