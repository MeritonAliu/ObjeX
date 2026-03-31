using System.Security;
using System.Text;
using ObjeX.Core.Models;

namespace ObjeX.Api.S3;

public static class S3Xml
{
    private static string Escape(string? value) =>
        SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    public static IResult ListBuckets(IEnumerable<Bucket> buckets)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<ListAllMyBucketsResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine("  <Owner><ID>owner</ID><DisplayName>owner</DisplayName></Owner>");
        xml.AppendLine("  <Buckets>");
        foreach (var bucket in buckets)
        {
            xml.AppendLine("    <Bucket>");
            xml.AppendLine($"      <Name>{Escape(bucket.Name)}</Name>");
            xml.AppendLine($"      <CreationDate>{bucket.CreatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</CreationDate>");
            xml.AppendLine("    </Bucket>");
        }
        xml.AppendLine("  </Buckets>");
        xml.AppendLine("</ListAllMyBucketsResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult ListObjectsV2(string bucket, IEnumerable<BlobObject> objects, IEnumerable<string> commonPrefixes, string? prefix, string? delimiter, string? continuationToken, string? startAfter)
    {
        var objList = objects.Where(o => !o.Key.EndsWith('/')).ToList();
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Name>{Escape(bucket)}</Name>");
        xml.AppendLine($"  <Prefix>{Escape(prefix)}</Prefix>");
        if (delimiter is not null)
            xml.AppendLine($"  <Delimiter>{Escape(delimiter)}</Delimiter>");
        xml.AppendLine("  <IsTruncated>false</IsTruncated>");
        xml.AppendLine($"  <KeyCount>{objList.Count}</KeyCount>");
        if (continuationToken is not null)
            xml.AppendLine($"  <ContinuationToken>{Escape(continuationToken)}</ContinuationToken>");
        if (startAfter is not null)
            xml.AppendLine($"  <StartAfter>{Escape(startAfter)}</StartAfter>");
        foreach (var obj in objList)
        {
            xml.AppendLine("  <Contents>");
            xml.AppendLine($"    <Key>{Escape(obj.Key)}</Key>");
            xml.AppendLine($"    <LastModified>{obj.UpdatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</LastModified>");
            xml.AppendLine($"    <ETag>&quot;{Escape(obj.ETag)}&quot;</ETag>");
            xml.AppendLine($"    <Size>{obj.Size}</Size>");
            xml.AppendLine("    <StorageClass>STANDARD</StorageClass>");
            xml.AppendLine("  </Contents>");
        }
        foreach (var cp in commonPrefixes)
        {
            xml.AppendLine("  <CommonPrefixes>");
            xml.AppendLine($"    <Prefix>{Escape(cp)}</Prefix>");
            xml.AppendLine("  </CommonPrefixes>");
        }
        xml.AppendLine("</ListBucketResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult ListObjects(string bucket, IEnumerable<BlobObject> objects, IEnumerable<string> commonPrefixes, string? prefix, string? delimiter)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Name>{Escape(bucket)}</Name>");
        xml.AppendLine($"  <Prefix>{Escape(prefix)}</Prefix>");
        xml.AppendLine($"  <Delimiter>{Escape(delimiter)}</Delimiter>");
        xml.AppendLine("  <IsTruncated>false</IsTruncated>");
        foreach (var obj in objects.Where(o => !o.Key.EndsWith('/')))
        {
            xml.AppendLine("  <Contents>");
            xml.AppendLine($"    <Key>{Escape(obj.Key)}</Key>");
            xml.AppendLine($"    <LastModified>{obj.UpdatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</LastModified>");
            xml.AppendLine($"    <ETag>&quot;{Escape(obj.ETag)}&quot;</ETag>");
            xml.AppendLine($"    <Size>{obj.Size}</Size>");
            xml.AppendLine("    <StorageClass>STANDARD</StorageClass>");
            xml.AppendLine("  </Contents>");
        }
        foreach (var cp in commonPrefixes)
        {
            xml.AppendLine("  <CommonPrefixes>");
            xml.AppendLine($"    <Prefix>{Escape(cp)}</Prefix>");
            xml.AppendLine("  </CommonPrefixes>");
        }
        xml.AppendLine("</ListBucketResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult Error(string code, string message, int statusCode = 400)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<Error>");
        xml.AppendLine($"  <Code>{Escape(code)}</Code>");
        xml.AppendLine($"  <Message>{Escape(message)}</Message>");
        xml.AppendLine("</Error>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8, statusCode);
    }

    public static IResult InitiateMultipartUpload(string bucket, string key, Guid uploadId)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<InitiateMultipartUploadResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Bucket>{Escape(bucket)}</Bucket>");
        xml.AppendLine($"  <Key>{Escape(key)}</Key>");
        xml.AppendLine($"  <UploadId>{uploadId}</UploadId>");
        xml.AppendLine("</InitiateMultipartUploadResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult CompleteMultipartUpload(string bucket, string key, string location, string etag)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<CompleteMultipartUploadResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Location>{Escape(location)}</Location>");
        xml.AppendLine($"  <Bucket>{Escape(bucket)}</Bucket>");
        xml.AppendLine($"  <Key>{Escape(key)}</Key>");
        xml.AppendLine($"  <ETag>&quot;{Escape(etag)}&quot;</ETag>");
        xml.AppendLine("</CompleteMultipartUploadResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult ListParts(string bucket, string key, Guid uploadId, IEnumerable<MultipartUploadPart> parts)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<ListPartsResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Bucket>{Escape(bucket)}</Bucket>");
        xml.AppendLine($"  <Key>{Escape(key)}</Key>");
        xml.AppendLine($"  <UploadId>{uploadId}</UploadId>");
        xml.AppendLine("  <IsTruncated>false</IsTruncated>");
        foreach (var part in parts.OrderBy(p => p.PartNumber))
        {
            xml.AppendLine("  <Part>");
            xml.AppendLine($"    <PartNumber>{part.PartNumber}</PartNumber>");
            xml.AppendLine($"    <LastModified>{part.UpdatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</LastModified>");
            xml.AppendLine($"    <ETag>&quot;{Escape(part.ETag)}&quot;</ETag>");
            xml.AppendLine($"    <Size>{part.Size}</Size>");
            xml.AppendLine("  </Part>");
        }
        xml.AppendLine("</ListPartsResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult BucketLocation()
    {
        return Results.Content(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<LocationConstraint xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">us-east-1</LocationConstraint>",
            "application/xml", Encoding.UTF8);
    }

    public static IResult DeleteResult(List<string> deleted, List<(string Key, string Code, string Message)> errors)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<DeleteResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        foreach (var key in deleted)
        {
            xml.AppendLine("  <Deleted>");
            xml.AppendLine($"    <Key>{Escape(key)}</Key>");
            xml.AppendLine("  </Deleted>");
        }
        foreach (var (key, code, message) in errors)
        {
            xml.AppendLine("  <Error>");
            xml.AppendLine($"    <Key>{Escape(key)}</Key>");
            xml.AppendLine($"    <Code>{Escape(code)}</Code>");
            xml.AppendLine($"    <Message>{Escape(message)}</Message>");
            xml.AppendLine("  </Error>");
        }
        xml.AppendLine("</DeleteResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }
}
