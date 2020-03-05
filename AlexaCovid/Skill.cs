using Alexa.NET;
using Alexa.NET.LocaleSpeech;
using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlexaCovid.Extensions;
using System.Net.Http;

namespace AlexaCovid
{
    public static class Skill
    {
        [FunctionName("AlexaCovid")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log)
        {
            var json = await req.ReadAsStringAsync();
            var skillRequest = JsonConvert.DeserializeObject<SkillRequest>(json);

            // Verifies that the request is indeed coming from Alexa.
            var isValid = await skillRequest.ValidateRequestAsync(req, log);
            if (!isValid)
            {
                return new BadRequestResult();
            }

            // Setup language resources.
            var store = SetupLanguageResources();
            var locale = skillRequest.CreateLocale(store);

            var request = skillRequest.Request;
            SkillResponse response = null;

            try
            {
                if (request is LaunchRequest launchRequest)
                {
                    log.LogInformation("Session started");

                    var welcomeMessage = await locale.Get(LanguageKeys.Welcome, null);
                    var welcomeRepromptMessage = await locale.Get(LanguageKeys.WelcomeReprompt, null);
                    response = ResponseBuilder.Ask(welcomeMessage, RepromptBuilder.Create(welcomeRepromptMessage));
                }
                else if (request is IntentRequest intentRequest)
                {
                    // Checks whether to handle system messages defined by Amazon.
                    var systemIntentResponse = await HandleSystemIntentsAsync(intentRequest, locale);
                    if (systemIntentResponse.IsHandled)
                    {
                        response = systemIntentResponse.Response;
                    }
                    else
                    {
                        // Processes request according to intentRequest.Intent.Name...
                        var message = await locale.Get(LanguageKeys.Response, null);
                        string slotValue = intentRequest.Intent.Slots["location"].Value;
                        if (!String.IsNullOrEmpty(slotValue))
                        {
                            slotValue = intentRequest.Intent.Slots["location"].Value.Trim().ToUpper();
                        }
                        log.LogInformation("Supplied Slot Value - " + slotValue);
                        string covidData = await GetCurrentData(slotValue, log).ConfigureAwait(false);
                        log.LogInformation("Data recvd - " + covidData);
                        response = ResponseBuilder.Tell(covidData);

                        // Note: The ResponseBuilder.Tell method automatically sets the
                        // Response.ShouldEndSession property to true, so the session will be
                        // automatically closed at the end of the response.
                    }
                }
                else if (request is SessionEndedRequest sessionEndedRequest)
                {
                    log.LogInformation("Session ended");
                    response = ResponseBuilder.Empty();
                }
            }
            catch
            {
                var message = await locale.Get(LanguageKeys.Error, null);
                response = ResponseBuilder.Tell(message);
                response.Response.ShouldEndSession = false;
            }

            return new OkObjectResult(response);
        }

        private static async Task<(bool IsHandled, SkillResponse Response)> HandleSystemIntentsAsync(IntentRequest request, ILocaleSpeech locale)
        {
            SkillResponse response = null;

            if (request.Intent.Name == BuiltInIntent.Cancel)
            {
                var message = await locale.Get(LanguageKeys.Cancel, null);
                response = ResponseBuilder.Tell(message);
            }
            else if (request.Intent.Name == BuiltInIntent.Help)
            {
                var message = await locale.Get(LanguageKeys.Help, null);
                response = ResponseBuilder.Ask(message, RepromptBuilder.Create(message));
            }
            else if (request.Intent.Name == BuiltInIntent.Stop)
            {
                var message = await locale.Get(LanguageKeys.Stop, null);
                response = ResponseBuilder.Tell(message);
            }

            return (response != null, response);
        }

        private static async Task<string> GetCurrentData(string Location, ILogger log)
        {
            String finalVal = String.Empty;
            HttpClientHandler handler = new HttpClientHandler();
            handler.UseDefaultCredentials = true;
            HttpClient client = new HttpClient(handler);

            try
            {
                HttpResponseMessage response = await client.GetAsync("https://www.ecdc.europa.eu/en/geographical-distribution-2019-ncov-cases");
                response.EnsureSuccessStatusCode();
                string retVal = await response.Content.ReadAsStringAsync();
                log.LogInformation("Data Table - " + retVal);


                retVal = retVal.Replace("\n", "");
                retVal = retVal.Replace("\t", "");
                int startTable = retVal.IndexOf("<th>Places reporting cases</th>");
                log.LogInformation("startTable - " + startTable);

                int startBody = retVal.IndexOf("<tbody>", startTable);
                log.LogInformation("startBody - " + startBody);

                int endBody = retVal.IndexOf("</tbody>", startBody + 10);
                log.LogInformation("endBody - " + endBody);

                string tableBody = retVal.Substring(startBody, endBody - startBody).ToUpper();
                log.LogInformation("tableBody - " + tableBody);

                //------------------------------------------------------------------------------------
                int locStart = tableBody.IndexOf(Location);
                int locEnd = tableBody.IndexOf("</TR>", locStart + 1);
                string locBody = tableBody.Substring(locStart, locEnd - locStart);
                locBody = locBody.Replace("</TD><TD>", "-").Replace("</TD>", "");
                finalVal = Location + " has " + locBody.Split("-".ToCharArray())[1] + " total positive cases and " + locBody.Split("-".ToCharArray())[2] + " deaths from Coronavirus till now.";
            }
            catch (Exception e)
            {
                log.LogError(e.Message + Environment.NewLine + e.StackTrace);
                finalVal = "Sorry, I could not find the data you are looking for.";
            }

            handler.Dispose();
            client.Dispose();

            return finalVal;
        }


        private static DictionaryLocaleSpeechStore SetupLanguageResources()
        {
            // Creates the locale speech store for each supported languages.
            var store = new DictionaryLocaleSpeechStore();

            store.AddLanguage("en", new Dictionary<string, object>
            {
                [LanguageKeys.Welcome] = "Welcome to the skill!",
                [LanguageKeys.WelcomeReprompt] = "You can ask help if you need instructions on how to interact with the skill",
                [LanguageKeys.Response] = "This is just a sample answer",
                [LanguageKeys.Cancel] = "Canceling...",
                [LanguageKeys.Help] = "Help...",
                [LanguageKeys.Stop] = "Bye bye!",
                [LanguageKeys.Error] = "I'm sorry, there was an unexpected error. Please, try again later."
            });

            store.AddLanguage("it", new Dictionary<string, object>
            {
                [LanguageKeys.Welcome] = "Benvenuto nella skill!",
                [LanguageKeys.WelcomeReprompt] = "Se vuoi informazioni sulle mie funzionalit�, prova a chiedermi aiuto",
                [LanguageKeys.Response] = "Questa � solo una risposta di prova",
                [LanguageKeys.Cancel] = "Sto annullando...",
                [LanguageKeys.Help] = "Aiuto...",
                [LanguageKeys.Stop] = "A presto!",
                [LanguageKeys.Error] = "Mi dispiace, si � verificato un errore imprevisto. Per favore, riprova di nuovo in seguito."
            });

            return store;
        }
    }
}
