namespace ZemaxMCP.Core.Exceptions;

public class ZemaxException : Exception
{
    public ZemaxException(string message) : base(message) { }
    public ZemaxException(string message, Exception innerException) : base(message, innerException) { }
}

public class ZemaxConnectionException : ZemaxException
{
    public ZemaxConnectionException(string message) : base(message) { }
    public ZemaxConnectionException(string message, Exception innerException) : base(message, innerException) { }
}

public class ZemaxRayTraceException : ZemaxException
{
    public ZemaxRayTraceException(string message) : base(message) { }
    public ZemaxRayTraceException(string message, Exception innerException) : base(message, innerException) { }
}

public class ZemaxOperandException : ZemaxException
{
    public string? OperandType { get; }

    public ZemaxOperandException(string message) : base(message) { }
    public ZemaxOperandException(string message, string operandType) : base(message)
    {
        OperandType = operandType;
    }
}
