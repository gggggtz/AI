﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.QnA;
using Microsoft.Bot.Solutions.Middleware.Telemetry;
using Newtonsoft.Json;

namespace Microsoft.Bot.Solutions.Middleware.Telemetry
{
    /// <summary>
    /// TelemetryQnaRecognizer invokes the Qna Maker and logs some results into Application Insights.
    /// Logs the score, and (optionally) question
    /// Along with Conversation and ActivityID.
    /// The Custom Event name this logs is "QnaMessage"
    /// See <seealso cref="QnaMaker"/> for additional information.
    /// </summary>
    public class TelemetryQnAMaker : QnAMaker, ITelemetryQnAMaker
    {
        public const string QnaMsgEvent = "QnaMessage";

        private QnAMakerEndpoint _endpoint;

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetryQnAMaker"/> class.
        /// </summary>
        /// <param name="endpoint">The endpoint of the knowledge base to query.</param>
        /// <param name="options">The options for the QnA Maker knowledge base.</param>
        /// <param name="logPersonalInformation">TRUE to include personally indentifiable information.</param>
        /// <param name="httpClient">An alternate client with which to talk to QnAMaker.
        /// If null, a default client is used for this instance.</param>
        public TelemetryQnAMaker(QnAMakerEndpoint endpoint, QnAMakerOptions options = null, bool logPersonalInformation = false, HttpClient httpClient = null)
            : base(endpoint, options, httpClient)
        {
            LogPersonalInformation = logPersonalInformation;

            _endpoint = endpoint;
        }

        public bool LogPersonalInformation { get; }

        public async Task<QueryResult[]> GetAnswersAsync(ITurnContext context)
        {
            // Call Qna Maker
            var queryResults = await base.GetAnswersAsync(context);

            // Find the Application Insights Telemetry Client
            if (queryResults != null && context.TurnState.TryGetValue(TelemetryLoggerMiddleware.AppInsightsServiceKey, out var telemetryClient))
            {
                var telemetryProperties = new Dictionary<string, string>();
                var telemetryMetrics = new Dictionary<string, double>();

                telemetryProperties.Add(QnATelemetryConstants.KnowledgeBaseIdProperty, _endpoint.KnowledgeBaseId);

                // Make it so we can correlate our reports with Activity or Conversation
                telemetryProperties.Add(QnATelemetryConstants.ActivityIdProperty, context.Activity.Id);
                var conversationId = context.Activity.Conversation.Id;
                if (!string.IsNullOrEmpty(conversationId))
                {
                    telemetryProperties.Add(QnATelemetryConstants.ConversationIdProperty, conversationId);
                }

                var text = context.Activity.Text;
                var userName = context.Activity.From.Name;

                // Use the LogPersonalInformation flag to toggle logging PII data, text and user name are common examples
                if (LogPersonalInformation)
                {
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        telemetryProperties.Add(QnATelemetryConstants.OriginalQuestionProperty, text);
                    }

                    if (!string.IsNullOrWhiteSpace(userName))
                    {
                        telemetryProperties.Add(QnATelemetryConstants.UsernameProperty, userName);
                    }
                }

                // Fill in Qna Results (found or not)
                if (queryResults.Length > 0)
                {
                    var queryResult = queryResults[0];
                    telemetryProperties.Add(QnATelemetryConstants.QuestionProperty, JsonConvert.SerializeObject(queryResult.Questions));
                    telemetryMetrics.Add(QnATelemetryConstants.QuestionIdProperty, queryResult.Id);
                    telemetryProperties.Add(QnATelemetryConstants.AnswerProperty, queryResult.Answer);
                    telemetryMetrics.Add(QnATelemetryConstants.ScoreProperty, queryResult.Score);
                    telemetryProperties.Add(QnATelemetryConstants.ArticleFoundProperty, "true");
                }
                else
                {
                    telemetryProperties.Add(QnATelemetryConstants.QuestionProperty, "No Qna Question matched");
                    telemetryMetrics.Add(QnATelemetryConstants.QuestionIdProperty, -1);
                    telemetryProperties.Add(QnATelemetryConstants.AnswerProperty, "No Qna Answer matched");
                    telemetryProperties.Add(QnATelemetryConstants.ArticleFoundProperty, "false");
                }

                // Track the event
                ((IBotTelemetryClient)telemetryClient).TrackEventEx(QnaMsgEvent, context.Activity, null, telemetryProperties, telemetryMetrics);
            }

            return queryResults;
        }
    }
}