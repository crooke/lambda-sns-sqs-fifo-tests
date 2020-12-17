using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.SNSEvents;
using Amazon.SimpleNotificationService;
using System.Linq;
using System.Diagnostics;
using Xunit.Abstractions;
using System;

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
            var topicArn = "arn:aws:sns:us-east-1:099157907345:MessageProcessor-SnsTopic-1ACCA72168YXE";
            var numConcurrentTasks = 5_000;

            output.WriteLine($"Start time: {DateTimeOffset.UtcNow}");
            var stopwatch = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, numConcurrentTasks).Select(i => snsClient.PublishAsync(topicArn, $"Message #{i}"));
            await Task.WhenAll(tasks);

            stopwatch.Stop();
            output.WriteLine($"End time: {DateTimeOffset.UtcNow}");
            output.WriteLine($"Time elapsed: {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
