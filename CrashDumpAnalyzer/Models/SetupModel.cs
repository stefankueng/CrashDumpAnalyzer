using System.ComponentModel.DataAnnotations;

namespace CrashDumpAnalyzer.Models
{
    public class SetupModel : IValidatableObject
    {
        [Required]
        [Display(Name = "SQLite connection string")]
        public string DefaultConnection { get; set; } = "Data Source=webapplication.db";

        [Display(Name = "Require users to sign in")]
        public bool UserAuthEnabled { get; set; } = true;

        [EmailAddress]
        [Display(Name = "Administrator email")]
        public string? AdminEmail { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Administrator password")]
        public string? AdminPassword { get; set; }

        [DataType(DataType.Password)]
        [Compare(nameof(AdminPassword))]
        [Display(Name = "Confirm password")]
        public string? ConfirmPassword { get; set; }

        [Display(Name = "SMTP host")]
        public string? SmtpHost { get; set; }

        [Range(1, 65535)]
        [Display(Name = "SMTP port")]
        public int SmtpPort { get; set; } = 587;

        [Display(Name = "SMTP username")]
        public string? SmtpUsername { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "SMTP password")]
        public string? SmtpPassword { get; set; }

        [Display(Name = "Use TLS for SMTP")]
        public bool SmtpEnableSsl { get; set; } = true;

        [EmailAddress]
        [Display(Name = "Sender email address")]
        public string? SmtpFrom { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (UserAuthEnabled)
            {
                if (string.IsNullOrWhiteSpace(AdminEmail))
                    yield return new ValidationResult("Enter the administrator email.", [nameof(AdminEmail)]);
                if (string.IsNullOrWhiteSpace(AdminPassword))
                    yield return new ValidationResult("Enter an administrator password.", [nameof(AdminPassword)]);
                if (string.IsNullOrWhiteSpace(ConfirmPassword))
                    yield return new ValidationResult("Confirm the administrator password.", [nameof(ConfirmPassword)]);
                if (string.IsNullOrWhiteSpace(SmtpHost))
                    yield return new ValidationResult("Enter an SMTP host for account confirmation and password reset emails.", [nameof(SmtpHost)]);
                if (string.IsNullOrWhiteSpace(SmtpFrom))
                    yield return new ValidationResult("Enter the sender email address.", [nameof(SmtpFrom)]);
            }
        }
    }
}
