internal sealed record RegressionCase(string Id, string Partition, Func<Task> Run);
