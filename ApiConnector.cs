using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Web;
using System.Windows.Input;
using Newtonsoft.Json.Linq;

namespace UsosApiBrowser
{
    /// <summary>
    /// Description of an USOS API Installation.
    /// </summary>
    public class ApiInstallation
    {
        /// <summary>
        /// Base URL of the Installation. Should end with a slash.
        /// </summary>
        public string base_url;

        /// <summary>
        /// USOS API version string (or null if unknown).
        /// </summary>
        public string version;
    }

    /// <summary>
    /// Description of a single argument of an USOS API method.
    /// </summary>
    public class ApiMethodArgument
    {
        public string name;
        public bool is_required;
        public string default_value;
        public string description;
    }

    /// <summary>
    /// Brief description of a single USOS API method.
    /// </summary>
    public class ApiMethod
    {
        /// <summary>
        /// Fully-qualified (begins with "services/") name of the method.
        /// </summary>
        public string name;

        /// <summary>
        /// Brief (single line), plain-text description of what the method does.
        /// </summary>
        public string brief_description;

        /// <summary>
        /// Short name of the method (last element of the fully-qualified name).
        /// </summary>
        public string short_name
        {
            get
            {
                var arr = this.name.Split('/');
                return arr[arr.Length-1];
            }
        }
    }

    /// <summary>
    /// Description of a single USOS API scope type.
    /// </summary>
    public class ApiScope
    {
        public string key;
        public string developers_description;
    }

    /// <summary>
    /// Full description of an USOS API method.
    /// </summary>
    public class ApiMethodFull : ApiMethod
    {
        /// <summary>
        /// A list of all possible arguments the method can be called with.
        /// This does not include standard OAuth signing arguments.
        /// </summary>
        public List<ApiMethodArgument> arguments = new List<ApiMethodArgument>();

        public string description;
        public string returns;
        public string ref_url;

        /// <summary>
        /// "required", "optional", "ignored"
        /// </summary>
        public string auth_options_consumer;

        /// <summary>
        /// "required", "optional", "ignored"
        /// </summary>
        public string auth_options_token;

        /// <summary>
        /// Is secure connection required to execute this method?
        /// </summary>
        public bool auth_options_ssl_required;
    }

    /// <summary>
    /// Implenentation of a simple USOS API connector. It cas generate properly signed
    /// OAuth requests and provides some USOS-API-specific helper functions.
    /// </summary>
    public class ApiConnector
    {
        /// <summary>
        /// Occurs when ApiConnector instance begins a web request.
        /// </summary>
        public event EventHandler BeginRequest;

        /// <summary>
        /// Occurs when ApiConnector instance ends previously started web request.
        /// </summary>
        public event EventHandler EndRequest;

        /// <summary>
        /// USOS API installation which the ApiConnector uses for method calls.
        /// </summary>
        public ApiInstallation currentInstallation;

        /// <summary>
        /// Create new USOS API connector.
        /// </summary>
        /// <param name="installation">
        ///     USOS API Installation which to initally use. This can be
        ///     switched later.
        /// </param>
        public ApiConnector(ApiInstallation installation)
        {
            this.currentInstallation = installation;
        }

        /// <summary>
        /// Switch connector to a different USOS API installation.
        /// </summary>
        public void SwitchInstallation(ApiInstallation apiInstallation)
        {
            this.currentInstallation = apiInstallation;
        }
        
        /// <summary>
        /// Read a WebResponse and return it's content as a string.
        /// </summary>
        public static string ReadResponse(WebResponse response)
        {
            if (response == null)
                return "";
            Stream stream = response.GetResponseStream();
            StreamReader reader = new StreamReader(stream);
            var s = reader.ReadToEnd();
            return s;
        }

        /// <summary>
        /// Make a request for the specified URL, read the response and return
        /// it's content as a string. Will throw a WebException if response is
        /// not status 200.
        /// </summary>
        public string GetResponse(string url)
        {
            this.BeginRequest(this, null);
            try
            {
                WebRequest request = WebRequest.Create(url);
                request.Timeout = 15000;
                request.Proxy = null;
                using (WebResponse response = request.GetResponse())
                {
                    return ReadResponse(response);
                }
            }
            catch (UriFormatException)
            {
                throw new WebException("Check your installation URL. Should start with http://");
            }
            finally
            {
                this.EndRequest(this, null);
            }
        }

        /// <summary>
        /// Construct a signed USOS API URL which points to a given method with
        /// given arguments.
        /// </summary>
        /// <param name="method">USOS API method to call.</param>
        /// <param name="args">A dictionary of method argument values for this call.</param>
        /// <param name="consumer_key">Your Consumer Key (if you want to sign this request).</param>
        /// <param name="consumer_secret">Your Consumer Secret (if you want to sign this request).</param>
        /// <param name="token">Your Token (if you want to sign this request).</param>
        /// <param name="token_secret">Your Token Secret (if you want to sign this request).</param>
        /// <returns></returns>
        public string GetURL(ApiMethod method, Dictionary<string, string> args = null,
            string consumer_key = "", string consumer_secret = "", string token = "",
            string token_secret = "", bool use_ssl = false)
        {
            var oauth = new OAuth.OAuthBase();
            if (args == null)
                args = new Dictionary<string, string>();

            var argPairsEncoded = new List<string>();
            foreach (var pair in args)
            {
                argPairsEncoded.Add(oauth.UrlEncode(pair.Key) + "=" + oauth.UrlEncode(pair.Value));
            }
            string url = this.currentInstallation.base_url + method.name;
            if (use_ssl)
                url = url.Replace("http://", "https://");
            if (argPairsEncoded.Count > 0)
                url += "?" + string.Join("&", argPairsEncoded);
            
            // We have our base version of the URL, with no OAuth arguments. Now we will
            // add standard OAuth stuff and sign it given Consumer Secret (and optionally
            // also with Token Secret).

            if (consumer_key == "")
                return url;

            string timestamp = oauth.GenerateTimeStamp();
            string nonce = oauth.GenerateNonce();
            string normalized_url;
            string normalized_params;
            string signature = oauth.GenerateSignature(new System.Uri(url), consumer_key,
                consumer_secret, token, token_secret, "GET", timestamp, nonce, out normalized_url,
                out normalized_params);
            url = this.currentInstallation.base_url;
            if (use_ssl)
                url = url.Replace("http://", "https://");
            url += method.name + "?" + normalized_params + "&oauth_signature=" + HttpUtility.UrlEncode(signature);

            return url;
        }

        /// <summary>
        /// Get a list of all public USOS API Installations. This list is propagated
        /// among all the installations, so it should be the same, no matter which
        /// installation you call this method on.
        /// </summary>
        public List<ApiInstallation> GetInstallations()
        {
            var results = new List<ApiInstallation>();
            var json = GetResponse(this.currentInstallation.base_url + "services/apisrv/installations");
            var installations = JArray.Parse(json);
            foreach (JObject installation in installations)
            {
                results.Add(new ApiInstallation{ base_url = (string)installation["base_url"] });
            }
            return results;
        }

        /// <summary>
        /// Get a list of all methods available in this installation.
        /// </summary>
        public List<ApiMethod> GetMethods()
        {
            var methods = new List<ApiMethod>();
            var json = GetResponse(this.currentInstallation.base_url + "services/apiref/method_index");
            var jmethods = JArray.Parse(json);
            foreach (JObject jmethod in jmethods)
            {
                methods.Add(new ApiMethod
                {
                    name = (string)jmethod["name"],
                    brief_description = (string)jmethod["brief_description"]
                });
            }
            return methods;
        }
    
        /// <summary>
        /// Get a full description of a specified method.
        /// </summary>
        /// <param name="method_name">Fully-qualified (should start with "services/") name of a method.</param>
        public ApiMethodFull GetMethod(string method_name)
        {
            var json = GetResponse(this.currentInstallation.base_url + "services/apiref/method?name=" + method_name);
            var jmethod = JObject.Parse(json);
            var jauthoptions = (JObject)jmethod["auth_options"];
            var method = new ApiMethodFull {
                name = (string)jmethod["name"],
                brief_description = (string)jmethod["brief_description"],
                description = (string)jmethod["description"],
                returns = (string)jmethod["returns"],
                ref_url = (string)jmethod["ref_url"],
                auth_options_consumer = (string)jauthoptions["consumer"],
                auth_options_token = (string)jauthoptions["token"],
                auth_options_ssl_required = (bool)jauthoptions["ssl_required"]
            };
            foreach (JObject jarg in (JArray)jmethod["arguments"])
            {
                method.arguments.Add(new ApiMethodArgument {
                    name = (string)jarg["name"],
                    is_required = (bool)jarg["is_required"],
                    description = (string)jarg["description"],
                    default_value = jarg["default_value"].ToString()
                });
            }
            return method;
        }

        /// <summary>
        /// Get a list of all valid scopes (permissions which a Consumer may request access to).
        /// </summary>
        public List<ApiScope> GetScopes()
        {
            var json = GetResponse(this.currentInstallation.base_url + "services/apiref/scopes");
            var jscopes = JArray.Parse(json);
            var scopes = new List<ApiScope>();
            foreach (JObject jscope in jscopes)
            {
                scopes.Add(new ApiScope
                {
                    key = (string)jscope["key"],
                    developers_description = (string)jscope["developers_description"],
                });
            }
            return scopes;
        }
    }
}