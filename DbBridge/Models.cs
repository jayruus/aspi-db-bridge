namespace DbBridge;

public record ExecReq(string QueryKey, Dictionary<string, object?>? Params);

public record ExecBatchReq(List<ExecReq> Ops);