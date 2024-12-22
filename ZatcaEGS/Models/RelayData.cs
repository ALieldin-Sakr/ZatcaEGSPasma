using Newtonsoft.Json;
using Zatca.eInvoice.Helpers;
using ZatcaEGS.Helpers;

namespace ZatcaEGS.Models
{
    public class RelayData
    {
        public string Referrer { get; set; }
        public string Key { get; set; }
        public string Data { get; set; }
        public string Callback { get; set; }
        public string Api { get; set; }
        public string Token { get; set; }

        public string BusinessDetails { get; set; }
        public string InvoiceJson { get; set; }
        public ManagerInvoice ManagerInvoice { get; set; }
        public string CertInfoString { get; set; }
        public PartyTaxInfo PartyInfo { get; set; }
        public string ApprovalStatus { get; set; }
        public string Base64QrCode { get; set; }
        public string ZatcaUUID { get; set; }
        public EnvironmentType EnvironmentType { get; set; }
        public int LastICV { get; set; } = 0;
        public string LastPIH { get; set; } = "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI0NjcyOWQ3M2EyN2ZiNTdlOQ==";

        public string DateCreated { get; set; }
        public bool HasTokenSecret { get; set; }
        public long EgsVersion { get; set; }
        public double InvoiceTotal { get; set; } = 0;

        public RelayData() { }

        public RelayData(Dictionary<string, string> formData)
        {
            Referrer = formData.GetValueOrDefault("Referrer");
            Key = formData.GetValueOrDefault("Key");
            Callback = formData.GetValueOrDefault("Callback");
            Data = formData.GetValueOrDefault("Data");
            Api = formData.GetValueOrDefault("Api");
            Token = formData.GetValueOrDefault("Token");

            // for rounding payable amount
            string invoiceView = formData.GetValueOrDefault("View");
            InvoiceTotal = ParseTotalValue(invoiceView);

            string DataString = JsonParser.UpdateJsonGuidValue(Data, ManagerCustomField.ZatcaUUIDGuid);

            var (businessDetails, dynamicParts) = JsonParser.ParseJson(DataString);

            BusinessDetails = businessDetails;

            var certString = JsonParser.FindStringByGuid(businessDetails, ManagerCustomField.CertificateInfoGuid);

            HasTokenSecret = !string.IsNullOrEmpty(JsonParser.FindStringByGuid(businessDetails, ManagerCustomField.TokenInfoGuid));

            var version = JsonParser.FindStringByGuid(businessDetails, ManagerCustomField.EgsVersionGuid);
            EgsVersion = VersionHelper.GetNumberOnly(JsonParser.FindStringByGuid(businessDetails, ManagerCustomField.EgsVersionGuid));

            if (!string.IsNullOrEmpty(certString))
            {
                CertificateInfo certificateInfo = ObjectCompressor.DeserializeFromBase64String<CertificateInfo>(certString);

                if (certificateInfo != null)
                {
                    var icv = JsonParser.FindStringByGuid(businessDetails, ManagerCustomField.LastIcvGuid) ?? LastICV.ToString();

                    LastICV = int.TryParse(icv, out int icvNumber) ? icvNumber : 0;
                    LastPIH = JsonParser.FindStringByGuid(businessDetails, ManagerCustomField.LastPihGuid) ?? LastPIH;

                    certificateInfo.ApiSecret = Token; //?? AccessToken;
                    certificateInfo.ApiEndpoint = Api; // ?? UrlHelper.GetApiEndpoint(Referrer);
                    certificateInfo.EnvironmentType = certificateInfo.EnvironmentType;

                    CertInfoString = ObjectCompressor.SerializeToBase64String(certificateInfo);
                }
            }

            // Retrieve the JSON string associated with a specific GUID
            InvoiceJson = dynamicParts.GetValueOrDefault(Key);

            if (InvoiceJson != null)
            {
                InvoiceJson = InvoiceJson.Replace("Customer", "InvoiceParty")
                                             .Replace("Supplier", "InvoiceParty")
                                             .Replace("SalesInvoice", "RefInvoice")
                                             .Replace("PurchaseInvoice", "RefInvoice")
                                             .Replace("SalesUnitPrice", "UnitPrice")
                                             .Replace("PurchaseUnitPrice", "UnitPrice");

                // Merge json
                InvoiceJson = JsonParser.ReplaceGuidValuesInJson(InvoiceJson, dynamicParts);
                InvoiceJson = JsonParser.ReplaceGuidValuesInJson(InvoiceJson, dynamicParts);

                ManagerInvoice = JsonConvert.DeserializeObject<ManagerInvoice>(InvoiceJson);
                ManagerInvoice.InvoiceTotal = InvoiceTotal;

                PartyInfo = new PartyTaxInfo()
                {
                    IdentificationScheme = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.IdentificationScheme, "RefInvoice"),
                    IdentificationID = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.IdentificationID, "RefInvoice"),

                    StreetName = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.StreetName, "RefInvoice"),
                    BuildingNumber = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.BuildingNumber, "RefInvoice"),
                    CitySubdivisionName = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.CitySubdivisionName, "RefInvoice"),
                    CityName = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.CityName, "RefInvoice"),
                    PostalZone = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.PostalZone, "RefInvoice"),
                    CountryIdentificationCode = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.CountryIdentificationCode, "RefInvoice"),
                    CompanyID = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.CompanyID, "RefInvoice"),
                    TaxSchemeID = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.TaxSchemeID, "RefInvoice"),
                    RegistrationName = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.RegistrationName, "RefInvoice"),
                    CurrencyCode = JsonParser.FindStringByGuid(InvoiceJson, "Code", "RefInvoice")
                };

                Base64QrCode = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.QrCodeGuid, "RefInvoice");
                DateCreated = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.DateCreatedGuid, "RefInvoice");

                if (!string.IsNullOrEmpty(Base64QrCode))
                {
                    ApprovalStatus = JsonParser.FindStringByGuid(InvoiceJson, ManagerCustomField.ApprovedInvoiceGuid, "RefInvoice");

                    ZatcaUUID = JsonParser.FindStringByGuid(InvoiceJson, Key, ManagerCustomField.ZatcaUUIDGuid);

                    if (!string.IsNullOrEmpty(ZatcaUUID) && ZatcaUUID.Contains('#'))
                    {
                        ZatcaUUID = ZatcaUUID.Replace("#", "");
                    }
                }
            }
            else
            {
                //Console.WriteLine($"GUID {Key} not found in dynamicParts.");
            }

        }
        
        private static double ParseTotalValue(string htmlContent)
        {
            // Find td element with id='Total'
            try
            {
                string pattern = @"<td[^>]*id=['""]Total['""][^>]*data-value=['""]([^'""]*)['""]";
                var match = System.Text.RegularExpressions.Regex.Match(htmlContent, pattern);

                if (!match.Success)
                    return 0;

                // Get the captured data-value
                string dataValue = match.Groups[1].Value;

                // Parse the value to decimal
                if (double.TryParse(dataValue, out double result))
                    return Math.Round(result, 2);
            }
            catch
            {
                return 0;
            }

            return 0;
        }


    }

    public class Currency
    {
        public string Code { get; set; } = "SAR";
        public string Name { get; set; }
        public string Symbol { get; set; }
    }

    public class CustomFields2
    {
        public Dictionary<string, string> Strings { get; set; } = [];
        public Dictionary<string, decimal> Decimals { get; set; } = [];
        public Dictionary<string, DateTime?> Dates { get; set; } = [];
        public Dictionary<string, bool> Booleans { get; set; } = [];
        public Dictionary<string, List<string>> StringArrays { get; set; } = [];
    }
    public class InvoiceParty
    {
        public string Name { get; set; }
        public Currency Currency { get; set; }
        public CustomFields2 CustomFields2 { get; set; }
    }

    public class LineItem
    {
        public string ItemCode { get; set; }
        public string Name { get; set; }
        public string ItemName { get; set; }
        public string UnitName { get; set; }
        public bool HasDefaultLineDescription { get; set; } = false;
        public string DefaultLineDescription { get; set; }
        public CustomFields2 CustomFields2 { get; set; }
    }

    public class TaxCode
    {
        public string Name { get; set; } = "";
        public int TaxRate { get; set; } = 0;
        public double Rate { get; set; } = 0;
    }

    public class Line
    {
        public LineItem Item { get; set; }
        public string LineDescription { get; set; }
        public Dictionary<string, string> CustomFields { get; set; }
        public CustomFields2 CustomFields2 { get; set; }
        public double Qty { get; set; } = 0;
        public double UnitPrice { get; set; } = 0;
        public double DiscountAmount { get; set; } = 0;
        public TaxCode TaxCode { get; set; }

    }

    public class LineValue
    {
        public double XmlQty { get; set; } = 0;
        public double XmlUnitPrice { get; set; } = 0;
        public double XmlDiscount { get; set; } = 0;
        public double XmlTaxRate { get; set; } = 0;
        public double XmlTaxableAmount { get; set; } = 0;
        public double XmlTaxAmount { get; set; } = 0;
        public double XmlRoundingAmount { get; set; } = 0;

        public LineValue(Line ln, bool Discount, bool AmountsIncludeTax)
        {
            XmlQty = ln.Qty;

            double taxRate = ln.TaxCode?.Rate ?? 0;
            XmlTaxRate = taxRate > 0 ? taxRate / 100 : 0;

            if (AmountsIncludeTax && XmlTaxRate > 0)
            {
                double disc = (Discount && ln.DiscountAmount > 0) ? ln.DiscountAmount / ln.Qty : 0;
                XmlDiscount = disc > 0 ? Math.Round(disc / (1 + XmlTaxRate), 2) : 0;

                double prc = ln.UnitPrice / (1 + XmlTaxRate);

                XmlUnitPrice = Math.Round(prc - XmlDiscount, 2);

                XmlTaxableAmount = Math.Round(XmlQty * XmlUnitPrice, 2);
            }
            else
            {
                XmlDiscount = (Discount && ln.DiscountAmount > 0) ? Math.Round(ln.DiscountAmount / ln.Qty, 2) : 0;
                XmlUnitPrice = Math.Round(ln.UnitPrice - XmlDiscount, 2);

                XmlTaxableAmount = Math.Round(XmlQty * XmlUnitPrice, 2);
            }

            XmlTaxAmount = Math.Round(XmlTaxableAmount * XmlTaxRate, 2);

            XmlRoundingAmount = XmlTaxableAmount + XmlTaxAmount;

        }
    }

    public class RefInvoice
    {
        public string Reference { get; set; }
    }

    public class ManagerInvoice
    {
        public DateTime IssueDate { get; set; }
        public DateTime DueDateDate { get; set; }

        public string Reference { get; set; }
        public RefInvoice RefInvoice { get; set; }
        public InvoiceParty InvoiceParty { get; set; }
        public string BillingAddress { get; set; }
        public double ExchangeRate { get; set; } = 1;
        public string Description { get; set; }
        public List<Line> Lines { get; set; }
        public bool HasLineNumber { get; set; } = false;
        public bool HasLineDescription { get; set; } = false;
        public bool Discount { get; set; } = false;
        public bool AmountsIncludeTax { get; set; } = false;
        public CustomFields2 CustomFields2 { get; set; }

        // payable rounding amount
        public double InvoiceTotal { get; set; }

    }
}