using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using Zatca.eInvoice;
using Zatca.eInvoice.Helpers;
using Zatca.eInvoice.Models;
using ZatcaEGS.Helpers;
using ZatcaEGS.Models;

namespace ZatcaEGS.Controllers
{
    public class SetupController : Controller
    {
        private readonly CsrGenerator _csrGenerator;
        private readonly HttpClient _httpClient = new();

        private readonly ILogger<SetupController> _logger;

        public SetupController(ILogger<SetupController> logger)
        {
            _logger = logger;
            _csrGenerator = new CsrGenerator();
        }

        [HttpGet("Setup/UpdateBusinessData")]
        public IActionResult UpdateBusinessData()
        {
            var svmJson = TempData["SetupViewModel"] as string;
            if (string.IsNullOrEmpty(svmJson))
            {
                // Handle the case where no TempData is found
                return Content("No TempData found."); // For debugging purposes
            }

            var viewModel = JsonConvert.DeserializeObject<SetupViewModel>(svmJson);

            _logger.LogInformation($"{viewModel.Api} - {HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"} - {viewModel.CertificateInfo.EnvironmentType}");

            return View(viewModel); // This will render Views/Setup/UpdateBusinessData.cshtml
        }


        [HttpPost("Setup/IntegrationSetup")]
        public IActionResult IntegrationSetup([FromForm] SetupViewModel viewModel)
        {
            var model = new CertificateInfo
            {
                ApiEndpoint = viewModel.Api,
                ApiSecret = viewModel.Token,
                IdentificationID = "1010010000",
                IdentificationScheme = "CRN",
                StreetName = "Prince Sultan",
                BuildingNumber = "2322",
                CitySubdivisionName = "Al-Murabba",
                CityName = "Riyadh",
                PostalZone = "23333",
                CountryIdentificationCode = "SA",
                CompanyID = "399999999900003",
                TaxSchemeID = "VAT",
                RegistrationName = "Maximum Speed Tech Supply LTD",
                BusinessCategory = "Supply activities",
                EnvironmentType = EnvironmentType.NonProduction,
                RelayURL = UrlHelper.GetRelayUrl(viewModel.Referrer)
            };

            _logger.LogInformation($"{viewModel.Api} - {HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"} - {viewModel.CertificateInfo.EnvironmentType}");

            viewModel.CertificateInfo = model;

            return View(viewModel);
        }


        [HttpPost("Setup/finish")]
        public IActionResult Finish(SetupViewModel viewModel)
        {
            CertificateInfo model = viewModel.CertificateInfo;
            if (ModelState.IsValid && !string.IsNullOrEmpty(model.PCSIDBinaryToken))
            {
                model.ApiSecret = null;
                var cert = ObjectCompressor.SerializeToBase64String(model);

                // Update businessDetails
                var businessDetails = viewModel.BusinessDetails;
                businessDetails = JsonParser.ModifyStringInEditData(businessDetails, "", ManagerCustomField.CertificateInfoGuid, cert);
                businessDetails = JsonParser.ModifyStringInEditData(businessDetails, "", ManagerCustomField.LastIcvGuid, "0");
                businessDetails = JsonParser.ModifyStringInEditData(businessDetails, "", ManagerCustomField.LastPihGuid, "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI0NjcyOWQ3M2EyN2ZiNTdlOQ==");
                viewModel.BusinessDetails = businessDetails;

                // Serialize BusinessDetails for JavaScript
                viewModel.BusinessDetailsJson = JsonConvert.SerializeObject(businessDetails);

                model.ApiSecret = viewModel.Token;
                // Console.WriteLine(viewModel.BusinessDetails);

                using (var memoryStream = new MemoryStream())
                {
                    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                    {
                        // File 1: cert.pem
                        var certEntry = archive.CreateEntry("cert.pem");
                        using (var writer = new StreamWriter(certEntry.Open()))
                        {
                            byte[] decodedBytes = Convert.FromBase64String(model.PCSIDBinaryToken);
                            string decodedToken = Encoding.UTF8.GetString(decodedBytes);
                            writer.Write(decodedToken);
                        }

                        // File 2: ec-secp256k1-priv-key.pem
                        var keyEntry = archive.CreateEntry("ec-secp256k1-priv-key.pem");
                        using (var writer = new StreamWriter(keyEntry.Open()))
                        {
                            writer.Write(model.EcSecp256k1Privkeypem);
                        }

                        // File 3: Original certificate info
                        var infoEntry = archive.CreateEntry($"{model.CsrCommonName}_{model.EnvironmentType}.txt");
                        using (var writer = new StreamWriter(infoEntry.Open()))
                        {
                            writer.WriteLine("Manager Certificate Info:");
                            writer.WriteLine(cert);
                            writer.WriteLine("\nOnboarding Device Info:");
                            foreach (var property in model.GetType().GetProperties())
                            {
                                if (property.Name != "ApiSecret" && property.Name != "ApiEndpoint")
                                {
                                    writer.WriteLine($"{property.Name}:");
                                    writer.WriteLine(property.GetValue(model));
                                    writer.WriteLine();
                                }
                            }
                        }
                    }

                    memoryStream.Position = 0;
                    var fileContent = memoryStream.ToArray();
                    viewModel.FileContent = Convert.ToBase64String(fileContent); // Convert file content to Base64
                    viewModel.Filename = $"{model.CsrCommonName}_{model.EnvironmentType}.zip";
                    viewModel.IsFileReady = true;
                }
            }
            return View("IntegrationSetup", viewModel);
        }


        [HttpGet("setup/GetCfData")]
        public string CustomFieldJson()
        {
            byte[] jsonDataBytes = ZatcaEGS.Properties.Resources.cfData;
            string jsonData = Encoding.UTF8.GetString(jsonDataBytes);
            return jsonData;
        }

        [HttpPost("setup/generatecsr")]
        public IActionResult GenerateCSR([FromBody] CsrGenerationDto csrData, [FromQuery] EnvironmentType environmentType)
        {
            if (csrData.IsValid(out var errors))
            {
                try
                {

                    if (environmentType == EnvironmentType.NonProduction)
                    {
                        csrData.OrganizationIdentifier = "399999999800003";
                        csrData.CommonName = string.Concat(csrData.CommonName.AsSpan(0, csrData.CommonName.Length - 15), "399999999800003");
                    }

                    var (csr, privateKey, errorMessages) = _csrGenerator.GenerateCsrAndPrivateKey(csrData, environmentType, false);
                    return Ok(new { Csr = csr, PrivateKey = privateKey, ErrorMessages = errorMessages });

                }
                catch (Exception ex)
                {
                    //Console.WriteLine("Error generating CSR: " + ex.Message);
                    return BadRequest("Error generating CSR: " + ex.Message);
                }
            }
            else
            {
                //Console.WriteLine("Invalid CSR data: " + string.Join(", ", errors));
                return BadRequest(new { Errors = errors });
            }
        }

        [HttpPost("setup/getccsid")]
        public async Task<IActionResult> GetCCSID([FromForm] SetupViewModel viewModel, [FromForm] string OTP)
        {
            try
            {
                CertificateInfo model = viewModel.CertificateInfo;

                // Get CCSID
                string jsonContent = JsonConvert.SerializeObject(new { csr = model.GeneratedCSR });

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
                _httpClient.DefaultRequestHeaders.Add("OTP", OTP);
                _httpClient.DefaultRequestHeaders.Add("Accept-Version", "V2");

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(model.ComplianceCSIDUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest("Error getting CCSID: " + response.ReasonPhrase);
                }

                var resultContent = await response.Content.ReadAsStringAsync();
                var zatcaResult = JsonConvert.DeserializeObject<ZatcaResultDto>(resultContent);

                var ccsidResult = new CCSIDResultDto
                {
                    CCSIDBinaryToken = zatcaResult.BinarySecurityToken,
                    CCSIDComplianceRequestId = zatcaResult.RequestID,
                    CCSIDSecret = zatcaResult.Secret
                };

                return Ok(ccsidResult);
            }
            catch
            {
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("setup/getpcsid")]
        public async Task<IActionResult> GetPCSID([FromForm] SetupViewModel viewModel)
        {
            try
            {
                CertificateInfo model = viewModel.CertificateInfo;

                //Invoice Compliance Check
                ComplianceCheckHelper ct = new ComplianceCheckHelper(model, model.CCSIDBinaryToken, model.EcSecp256k1Privkeypem);
                string invoiceHash = null;
                int iICV = 0;

                //10000 Clearance Standard

                if (model.CsrInvoiceType.StartsWith('1'))
                {
                    iICV += 1;
                    ZatcaRequestApi InvDebitNote = ct.GetRequestApi("DN-202408-0001", "PCH-202408-0001", InvoiceType.TaxInvoiceDebitNote, "0100000", iICV, "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI0NjcyOWQ3M2EyN2ZiNTdlOQ==");
                    invoiceHash = await PostComplianceCheck(model.ComplianceCheckUrl, InvDebitNote, model.CCSIDBinaryToken, model.CCSIDSecret);

                    if (!string.IsNullOrEmpty(invoiceHash))
                    {
                        iICV += 1;
                        ZatcaRequestApi InvSales = ct.GetRequestApi("INV-202408-0001", null, InvoiceType.TaxInvoice, "0100000", iICV, invoiceHash);
                        invoiceHash = await PostComplianceCheck(model.ComplianceCheckUrl, InvSales, model.CCSIDBinaryToken, model.CCSIDSecret);
                        if (!string.IsNullOrEmpty(invoiceHash))
                        {
                            iICV += 1;
                            ZatcaRequestApi InvCreditNote = ct.GetRequestApi("CN-202408-0001", "INV-202408-0001", InvoiceType.TaxInvoiceCreditNote, "0100000", iICV, invoiceHash);
                            invoiceHash = await PostComplianceCheck(model.ComplianceCheckUrl, InvCreditNote, model.CCSIDBinaryToken, model.CCSIDSecret);
                            if (string.IsNullOrEmpty(invoiceHash))
                            {
                                return null;
                            }
                        }
                    }
                }

                //01000 || 11000  Reporting Simplified 

                if (model.CsrInvoiceType.Substring(1, 1) == "1")
                {

                    if (string.IsNullOrEmpty(invoiceHash))
                    {
                        invoiceHash = "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI0NjcyOWQ3M2EyN2ZiNTdlOQ==";
                        iICV = 0;
                    };

                    iICV += 1;
                    ZatcaRequestApi InvDebitNote = ct.GetRequestApi("DN-202408-0001", "PCH-202408-0001", InvoiceType.TaxInvoiceDebitNote, "0200000", iICV, invoiceHash);
                    invoiceHash = await PostComplianceCheck(model.ComplianceCheckUrl, InvDebitNote, model.CCSIDBinaryToken, model.CCSIDSecret);

                    if (!string.IsNullOrEmpty(invoiceHash))
                    {
                        iICV += 1;
                        ZatcaRequestApi InvSales = ct.GetRequestApi("INV-202408-0001", null, InvoiceType.TaxInvoice, "0200000", iICV, invoiceHash);
                        invoiceHash = await PostComplianceCheck(model.ComplianceCheckUrl, InvSales, model.CCSIDBinaryToken, model.CCSIDSecret);
                        if (!string.IsNullOrEmpty(invoiceHash))
                        {
                            iICV += 1;
                            ZatcaRequestApi InvCreditNote = ct.GetRequestApi("CN-202408-0001", "INV-202408-0001", InvoiceType.TaxInvoiceCreditNote, "0200000", iICV, invoiceHash);
                            invoiceHash = await PostComplianceCheck(model.ComplianceCheckUrl, InvCreditNote, model.CCSIDBinaryToken, model.CCSIDSecret);
                            if (string.IsNullOrEmpty(invoiceHash))
                            {
                                return null;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(invoiceHash))
                {
                    return null;
                }

                // Get PCSID

                string jsonContent = JsonConvert.SerializeObject(new { compliance_request_id = model.CCSIDComplianceRequestId });

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
                _httpClient.DefaultRequestHeaders.Add("Accept-Version", "V2");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{model.CCSIDBinaryToken}:{model.CCSIDSecret}")));

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(model.ProductionCSIDUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest("Error getting PCSID: " + response.ReasonPhrase);
                }

                string resultContent = await response.Content.ReadAsStringAsync();
                ZatcaResultDto zatcaResult = JsonConvert.DeserializeObject<ZatcaResultDto>(resultContent);

                //Console.WriteLine(resultContent);

                var pcsidResult = new PCSIDResultDto
                {
                    PCSIDBinaryToken = zatcaResult.BinarySecurityToken,
                    PCSIDSecret = zatcaResult.Secret,
                    RegisteredDate = DateTime.Now,
                };

                return Ok(pcsidResult);
            }

            catch
            {
                return StatusCode(500, "Internal server error");
            }
        }

        public async Task<string> PostComplianceCheck_Old(string ComplianceCheckUrl, ZatcaRequestApi RequestApi, string CCSIDBinaryToken, string CCSIDSecret)
        {
            try
            {

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
                _httpClient.DefaultRequestHeaders.Add("Accept-Version", "V2");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{CCSIDBinaryToken}:{CCSIDSecret}")));

                var jsonContent = JsonConvert.SerializeObject(RequestApi);

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ComplianceCheckUrl, content);

                var resultContent = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonConvert.DeserializeObject<ServerResult>(resultContent);

                //Console.WriteLine(resultContent);

                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    Formatting = Formatting.Indented
                };

                var jsonResult = JsonConvert.SerializeObject(apiResponse, settings);

                //Console.WriteLine(jsonResult);

                if (apiResponse.ClearanceStatus == "CLEARED" || apiResponse.ReportingStatus == "REPORTED")
                {
                    return RequestApi.invoiceHash;
                }

                return null;
            }
            catch
            {
                //Console.WriteLine(ex.Message);
                return null;
            }
        }

        public async Task<string> PostComplianceCheck(string ComplianceCheckUrl, ZatcaRequestApi RequestApi, string CCSIDBinaryToken, string CCSIDSecret)
        {
            const int maxRetryAttempts = 3; // Maximum retry attempts
            const int delayMilliseconds = 1000; // Initial delay time in milliseconds
            int retryCount = 0; // Tracks the retry attempts

            while (retryCount < maxRetryAttempts)
            {
                try
                {
                    // Clear and set headers for each retry attempt
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
                    _httpClient.DefaultRequestHeaders.Add("Accept-Version", "V2");
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{CCSIDBinaryToken}:{CCSIDSecret}")));

                    // Serialize the request
                    var jsonContent = JsonConvert.SerializeObject(RequestApi);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    // Attempt to send the request
                    var response = await _httpClient.PostAsync(ComplianceCheckUrl, content);
                    var resultContent = await response.Content.ReadAsStringAsync();
                    var apiResponse = JsonConvert.DeserializeObject<ServerResult>(resultContent);

                    //var settings = new JsonSerializerSettings
                    //{
                    //    NullValueHandling = NullValueHandling.Ignore,
                    //    Formatting = Formatting.Indented
                    //};
                    //var jsonResult = JsonConvert.SerializeObject(apiResponse, settings);
                    // Console.WriteLine(jsonResult); // For debugging

                    // Check response for success conditions
                    if (apiResponse.ClearanceStatus == "CLEARED" || apiResponse.ReportingStatus == "REPORTED")
                    {
                        return RequestApi.invoiceHash;
                    }

                    return null; // Unsuccessful, return null
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TimeoutException)
                {
                    // Network-related exceptions can be retried
                    retryCount++;
                    Console.WriteLine($"Network attempt {retryCount} failed: {ex.Message}");
                    if (retryCount >= maxRetryAttempts)
                    {
                        return null;
                    }
                    await Task.Delay(delayMilliseconds * (int)Math.Pow(2, retryCount));
                }
                catch (JsonSerializationException ex)
                {
                    // Serialization issues should not be retried
                    Console.WriteLine("Serialization error: " + ex.Message);
                    return null;
                }
                catch (AuthenticationException ex)
                {
                    // Authentication errors should not be retried
                    Console.WriteLine("Authentication error: " + ex.Message);
                    return null;
                }

            }
            return null;
        }

    }
}