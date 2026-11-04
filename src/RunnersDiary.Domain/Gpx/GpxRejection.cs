namespace RunnersDiary.Domain.Gpx;

/// <summary>Why a GPX file was rejected. Serialized by name (camelCase) as the <c>code</c> of the invalid-gpx problem.</summary>
public enum GpxRejectionCode
{
    MalformedXml,
    DtdProhibited,
    UnsupportedFormat,
    NotGpx,
    TooLarge,
    TooManyPoints,
    TooFewPoints,
    InvalidCoordinate,
    TooComplex,
    WrongExtension,
}

/// <summary>A rejection with a fixed English message. Parser exception text is never carried over.</summary>
public sealed record GpxRejection(GpxRejectionCode Code, string Message)
{
    public static GpxRejection For(GpxRejectionCode code) => new(code, MessageFor(code));

    /// <summary>camelCase code, e.g. <c>notGpx</c>.</summary>
    public string CodeName => char.ToLowerInvariant(Code.ToString()[0]) + Code.ToString()[1..];

    private static string MessageFor(GpxRejectionCode code) => code switch
    {
        GpxRejectionCode.MalformedXml => "The file is not well-formed XML.",
        GpxRejectionCode.DtdProhibited => "The file contains a DOCTYPE declaration, which is not allowed.",
        GpxRejectionCode.UnsupportedFormat => "The file is not a plain XML GPX file (compressed or binary files are not supported).",
        GpxRejectionCode.NotGpx => "The file is not a GPX 1.0 or 1.1 document.",
        GpxRejectionCode.TooLarge => "The file is too large.",
        GpxRejectionCode.TooManyPoints => "The file contains too many track points.",
        GpxRejectionCode.TooFewPoints => "The file must contain at least two track or route points.",
        GpxRejectionCode.InvalidCoordinate => "The file contains an invalid coordinate.",
        GpxRejectionCode.TooComplex => "The file structure is too complex.",
        GpxRejectionCode.WrongExtension => "The file name must end in .gpx.",
        _ => "The file was rejected.",
    };
}
