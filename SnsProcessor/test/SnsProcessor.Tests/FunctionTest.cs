using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.SNSEvents;
using Amazon.SimpleNotificationService;
using Amazon.DynamoDBv2;
using System.Linq;
using System.Diagnostics;
using Xunit.Abstractions;
using System;
using Amazon.DynamoDBv2.Model;
using System.Threading;
using Amazon.SimpleNotificationService.Model;

namespace SnsProcessor.Tests
{
    public class FunctionTest
    {
        private readonly ITestOutputHelper output;

        public FunctionTest(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task TestSQSEventLambdaFunction()
        {
            var snsEvent = new SNSEvent
            {
                Records = new List<SNSEvent.SNSRecord>
                {
                    new SNSEvent.SNSRecord
                    {
                        Sns = new SNSEvent.SNSMessage()
                        {
                            Message = "foobar"
                        }
                    }
                }
            };

            var logger = new TestLambdaLogger();
            var context = new TestLambdaContext
            {
                Logger = logger
            };

            var function = new Function();
            await function.FunctionHandler(snsEvent, context);

            Assert.Contains("Processed record foobar", logger.Buffer.ToString());
        }

        [Fact]
        public async Task BenchmarkTestAsync()
        {
            System.Environment.SetEnvironmentVariable("AWS_PROFILE", "dev-enc");
            var snsClient = new AmazonSimpleNotificationServiceClient();
            var dynamoClient = new AmazonDynamoDBClient();
            var topicArn = "arn:aws:sns:us-east-1:099157907345:MessageProcessor-SnsTopic-1ACCA72168YXE";
            var messagesToSend = 1000;
            var messagesSent = 0;

            var startTime = DateTimeOffset.UtcNow.ToString("o");
            var testId = Guid.NewGuid().ToString();
            var put1 = dynamoClient.PutItemAsync(new PutItemRequest
            {
                TableName = "message-processor",
                Item = new Dictionary<string, AttributeValue>
                {
                    { "pk", new AttributeValue($"SnsProcessor|{testId}") },
                    { "TestId", new AttributeValue(testId) },
                    { "StartTime", new AttributeValue(startTime) },
                    { "MessageCount", new AttributeValue { N = "0" }},
                    { "MessagesToSend", new AttributeValue { N = messagesToSend.ToString() }},
                }
            });
            var put2 = dynamoClient.PutItemAsync(new PutItemRequest
            {
                TableName = "message-processor",
                Item = new Dictionary<string, AttributeValue>
                {
                    { "pk", new AttributeValue($"SqsProcessor|{testId}") },
                    { "TestId", new AttributeValue(testId) },
                    { "StartTime", new AttributeValue(startTime) },
                    { "MessageCount", new AttributeValue { N = "0" }},
                    { "MessagesToSend", new AttributeValue { N = messagesToSend.ToString() }},
                }
            });
            await Task.WhenAll(put1, put2);

            output.WriteLine($"Start time: {startTime}");
            var stopwatch = Stopwatch.StartNew();

            var publishTasks = Enumerable.Range(0, messagesToSend).Select(async i =>
            {
                try
                {
                    var message = System.Text.Json.JsonSerializer.Serialize(new Function.TestMessage
                    {
                        StartTime = DateTimeOffset.Parse(startTime),
                        TestId = testId
                    });
                    await snsClient.PublishAsync(topicArn, message);
                    Interlocked.Increment(ref messagesSent);
                }
                catch (Exception e)
                {
                    output.WriteLine("Exception: {0} BaseException: {1}", e.Message, e.GetBaseException());
                }
            });
            await Task.WhenAll(publishTasks);

            stopwatch.Stop();
            output.WriteLine($"Test ID: {testId}");
            output.WriteLine($"End time: {DateTimeOffset.UtcNow}");
            output.WriteLine($"Time elapsed: {stopwatch.ElapsedMilliseconds} ms");

            var update1 = dynamoClient.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = "message-processor",
                Key = new Dictionary<string, AttributeValue>
                {
                    { "pk", new AttributeValue($"SnsProcessor|{testId}") }
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":sent", new AttributeValue { N = messagesSent.ToString() }},
                },
                UpdateExpression = "SET MessagesSent = :sent"
            });
            var update2 = dynamoClient.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = "message-processor",
                Key = new Dictionary<string, AttributeValue>
                {
                    { "pk", new AttributeValue($"SqsProcessor|{testId}") }
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":sent", new AttributeValue { N = messagesSent.ToString() }},
                },
                UpdateExpression = "SET MessagesSent = :sent"
            });
            await Task.WhenAll(update1, update2);
        }

        [Fact]
        public async Task FifoBenchmarkTestAsync()
        {
            System.Environment.SetEnvironmentVariable("AWS_PROFILE", "dev-enc");
            var snsClient = new AmazonSimpleNotificationServiceClient();
            var dynamoClient = new AmazonDynamoDBClient();
            var topicArn = "arn:aws:sns:us-east-1:099157907345:MessageProcessor-SnsFifoTopic.fifo";
            var messagesToSend = 4000;
            var messagesSent = 0;

            var startTime = DateTimeOffset.UtcNow.ToString("o");
            var testId = Guid.NewGuid().ToString();
            await dynamoClient.PutItemAsync(new PutItemRequest
            {
                TableName = "message-processor",
                Item = new Dictionary<string, AttributeValue>
                {
                    { "pk", new AttributeValue($"SqsProcessor|{testId}") },
                    { "TestId", new AttributeValue(testId) },
                    { "StartTime", new AttributeValue(startTime) },
                    { "MessageCount", new AttributeValue { N = "0" }},
                    { "MessagesToSend", new AttributeValue { N = messagesToSend.ToString() }},
                    { "IsFifo", new AttributeValue { BOOL = true }},
                }
            });

            output.WriteLine($"Start time: {startTime}");
            var stopwatch = Stopwatch.StartNew();

            var publishTasks = Enumerable.Range(0, messagesToSend).Select(async i =>
            {
                try
                {
                    var message = System.Text.Json.JsonSerializer.Serialize(new Function.TestMessage
                    {
                        StartTime = DateTimeOffset.Parse(startTime),
                        TestId = testId,
                    });
                    await snsClient.PublishAsync(new PublishRequest
                    {
                        TopicArn = topicArn,
                        Message = message,
                        MessageGroupId = testId,
                        MessageDeduplicationId = $"{testId}#{i}"
                    });
                    Interlocked.Increment(ref messagesSent);
                }
                catch (Exception e)
                {
                    output.WriteLine("Exception: {0} BaseException: {1}", e.Message, e.GetBaseException());
                }
            });
            await Task.WhenAll(publishTasks);

            stopwatch.Stop();
            output.WriteLine($"Test ID: {testId}");
            output.WriteLine($"End time: {DateTimeOffset.UtcNow}");
            output.WriteLine($"Time elapsed: {stopwatch.ElapsedMilliseconds} ms");

            await dynamoClient.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = "message-processor",
                Key = new Dictionary<string, AttributeValue>
                {
                    { "pk", new AttributeValue($"SqsProcessor|{testId}") }
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":sent", new AttributeValue { N = messagesSent.ToString() }},
                },
                UpdateExpression = "SET MessagesSent = :sent"
            });
        }
    }
}
