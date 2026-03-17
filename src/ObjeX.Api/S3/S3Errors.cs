namespace ObjeX.Api.S3;

public static class S3Errors
{
    public const string NoSuchBucket = "NoSuchBucket";
    public const string NoSuchKey = "NoSuchKey";
    public const string BucketAlreadyExists = "BucketAlreadyExists";
    public const string BucketNotEmpty = "BucketNotEmpty";
    public const string InvalidBucketName = "InvalidBucketName";
    public const string InvalidArgument = "InvalidArgument";
    public const string AccessDenied = "AccessDenied";
    public const string InvalidAccessKeyId = "InvalidAccessKeyId";
    public const string SignatureDoesNotMatch = "SignatureDoesNotMatch";
    public const string RequestExpired = "RequestExpired";
    public const string EntityTooLarge = "EntityTooLarge";
    public const string InternalError = "InternalError";
}
