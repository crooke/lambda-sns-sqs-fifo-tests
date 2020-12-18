using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace StreamHandler
{
    public class Function
    {
        private readonly AmazonDynamoDBClient amazonDynamoDB = new AmazonDynamoDBClient();

        public async Task FunctionHandlerAsync(DynamoDBEvent dynamoDBEvent, ILambdaContext context)
        {
            var tasks = dynamoDBEvent.Records.Select(record => ProcessRecordAsync(record, context));
            await Task.WhenAll(tasks);
        }

        private async Task ProcessRecordAsync(DynamoDBEvent.DynamodbStreamRecord record, ILambdaContext context)
        {
            if (record.EventName == OperationType.MODIFY)
            {
                var newImage = record.Dynamodb.NewImage;
                if (!newImage.ContainsKey("ElapsedTime") && newImage.ContainsKey("MessagesSent") && int.Parse(newImage["MessageCount"].N) >= int.Parse(newImage["MessagesSent"].N))
                {
                    // This means we're done processing all messages. Calculate the elapsed time.
                    var elapsed = DateTimeOffset.Parse(newImage["EndTime"].S) - DateTimeOffset.Parse(newImage["StartTime"].S);
                    await amazonDynamoDB.UpdateItemAsync(new UpdateItemRequest
                    {
                        TableName = "message-processor",
                        Key = record.Dynamodb.Keys,
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            { ":elapsed", new AttributeValue { N = elapsed.TotalMilliseconds.ToString() }}
                        },
                        UpdateExpression = "SET ElapsedTime = :elapsed"
                    });
                }
            }
        }
    }
}
