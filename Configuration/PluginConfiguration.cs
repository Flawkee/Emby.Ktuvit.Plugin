using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Validation;
using Ktuvit.Plugin.Helpers;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Ktuvit.Plugin.Configuration
{
    public class PluginConfiguration : EditableOptionsBase
    {

        public override string EditorTitle => "Ktuvit Plugin Configuration";

        public override string EditorDescription => "Automatically downloads Hebrew subtitles from Ktuvit.me.\n\n"
                                                    + "Login credentials (username and password) are only required for movie subtitles.\n"
                                                    + "If credentials are not provided, the plugin will still download series subtitles.\n\n";

        [DisplayName("Ktuvit Request Timeout")]
        [Description("Request Timeout (in seconds): If not configured, the default is 5 seconds. This should be set and saved before setting up username and password. This setting is helpful if Ktuvit.me is unavailable, ensuring that subtitle search doesn't wait the full 30 seconds (the Emby default) before returning a response.")]
        public int? requestTimeout { get; set; }
        [DisplayName("Ktuvit Username")]
        [Description("Email address registered in Ktuvit.me")]
        public string Username { get; set; }
        [DisplayName("Ktuvit Password")]
        [IsPassword]
        public string Password { get; set; }
        protected override void Validate(ValidationContext context)
        {
            var explorer = KtuvitExplorer.Instance;
            if (explorer == null)
            {
                context.AddValidationError("KtuvitExplorer is not initialized.");
                return;
            }

            // allow empty username and password (for series subtitles only)
            if (string.IsNullOrEmpty(Username) && string.IsNullOrEmpty(Password))
            {
                return;
            }

            if (requestTimeout.HasValue && (requestTimeout.Value <= 0 || requestTimeout.Value >= 30))
            {
                context.AddValidationError("Request Timeout must be a greater than 0 and lower than 30 seconds.");
                return;
            }
            var accessStatus = explorer.KtuvitAccessValidation();
            if (!accessStatus)
            {
                context.AddValidationError("Could not reach Ktuvit.me with the request timeout configured. Ktuvit.me might be unavailable, Please try again later.");
            }
            else
            {
                var authenticationStatus = explorer.KtuvitAuthentication(Username, Password);
                if (!authenticationStatus)
                {
                    context.AddValidationError("Failed to authenticate Ktuvit.me. Please validate your credentials.");
                }
            }
        }
    }
}