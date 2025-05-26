using Microsoft.VisualBasic;
using Zatca.eInvoice.Helpers;
using Zatca.eInvoice.Models;
using ZatcaEGS.Models;
using static ZatcaEGS.Helpers.VATInfoHelper;

namespace ZatcaEGS.Helpers
{
    public class RelayToInvoiceMapper
    {
        private readonly RelayData _relayData;
        private readonly CertificateInfo _certInfo;

        private readonly ManagerInvoice _managerInvoice;
        private readonly string _invoiceCurrencyCode;
        private readonly string _taxCurrencyCode;

        public RelayToInvoiceMapper(RelayData relayData)
        {
            _relayData = relayData;

            _certInfo = ObjectCompressor.DeserializeFromBase64String<CertificateInfo>(relayData.CertInfoString);

            _managerInvoice = relayData.ManagerInvoice;
            _invoiceCurrencyCode = _managerInvoice.InvoiceParty.Currency?.Code ?? "SAR";
            _taxCurrencyCode = "SAR";
        }

        public Invoice GenerateInvoiceObject()
        {
            int invoiceType = 388;
            if (_relayData.Referrer.Contains("debit-note"))
            {
                invoiceType = 383;
            }
            else if (_relayData.Referrer.Contains("credit-note"))
            {
                invoiceType = 381;
            }

            string invoiceSubType = JsonParser.FindStringByGuid(_relayData.InvoiceJson, ManagerCustomField.InvoiceSubTypeGuid, "RefInvoice") ?? "0200000";

            string dateCreated = null;
            string timeCreated = null;

            if (!string.IsNullOrEmpty(_relayData.DateCreated) && _relayData.DateCreated.Contains(' '))
            {
                var dateTimeParts = _relayData.DateCreated.Split(' ');
                dateCreated = dateTimeParts[0];
                timeCreated = dateTimeParts[1];
            }

            Invoice invoice = new()
            {
                ProfileID = "reporting:1.0",
                ID = new ID(_managerInvoice.Reference),
                UUID = _relayData.ZatcaUUID,

                IssueDate = dateCreated ?? _managerInvoice.IssueDate.ToString("yyyy-MM-dd"),
                IssueTime = timeCreated ?? "00:00:00",

                InvoiceTypeCode = new InvoiceTypeCode((InvoiceType)invoiceType, invoiceSubType),
                DocumentCurrencyCode = _invoiceCurrencyCode,
                TaxCurrencyCode = _taxCurrencyCode
            };

            string InvoiceRef = _managerInvoice.RefInvoice?.Reference;
            if (InvoiceRef != null)
            {
                invoice.BillingReference = new BillingReference
                {
                    InvoiceDocumentReference = new InvoiceDocumentReference
                    {
                        ID = new ID(InvoiceRef)
                    }
                };
            }

            invoice.AdditionalDocumentReference = CreateAdditionalDocumentReferences().ToArray();

            invoice.AccountingSupplierParty = CreateAccountingSupplierParty();

            invoice.AccountingCustomerParty = CreateAccountingCustomerParty();

            invoice.Delivery = new Delivery();
            invoice.Delivery.ActualDeliveryDate = _managerInvoice.IssueDate.ToString("yyyy-MM-dd");

            if (DateAndTime.Year(_managerInvoice.DueDateDate) < 2024)
            {
                invoice.Delivery.LatestDeliveryDate = _managerInvoice.IssueDate.ToString("yyyy-MM-dd");
            }
            else
            {
                invoice.Delivery.LatestDeliveryDate = _managerInvoice.DueDateDate.ToString("yyyy-MM-dd");
            }

            invoice.PaymentMeans = CreatePaymentMeans();

            invoice.InvoiceLine = CreateInvoiceLines().ToArray();
            invoice.TaxTotal = CalculateTaxTotals().ToArray();
            invoice.LegalMonetaryTotal = CalculateLegalMonetaryTotal();

            if (invoice?.TaxTotal != null)
            {
                // Define the codes to check against
                var validCodes = new[] { "VATEX-SA-EDU", "VATEX-SA-HEA" }; // NAT

                // LINQ to find if any valid exemption reason code exists
                bool found = invoice.TaxTotal
                    .Where(taxTotal => taxTotal.TaxSubtotal != null) // Ensure TaxSubtotal is not null
                    .SelectMany(taxTotal => taxTotal.TaxSubtotal) // Flatten the array of TaxSubtotals
                    .Any(subtotal => subtotal.TaxCategory != null &&
                                     !string.IsNullOrEmpty(subtotal.TaxCategory.TaxExemptionReasonCode) &&
                                     validCodes.Contains(subtotal.TaxCategory.TaxExemptionReasonCode));

                if (found)
                {
                    AccountingCustomerParty partyID = invoice.AccountingCustomerParty;
                    PartyTaxInfo partyInfo = _relayData.PartyInfo;

                    partyID.Party.PartyIdentification = new PartyIdentification
                    {
                        ID = new ID
                        {
                            SchemeID = partyInfo.IdentificationScheme,
                            Value = partyInfo.IdentificationID
                        }
                    };
                }
            }

            return invoice;
        }

        private List<AdditionalDocumentReference> CreateAdditionalDocumentReferences()
        {
            List<AdditionalDocumentReference> references = new();

            AdditionalDocumentReference referenceICV = new()
            {
                ID = new ID("ICV"),
                UUID = (_relayData.LastICV + 1).ToString()
            };
            references.Add(referenceICV);

            AdditionalDocumentReference referencePIH = new()
            {
                ID = new ID("PIH"),
                Attachment = new Attachment
                {
                    EmbeddedDocumentBinaryObject = new EmbeddedDocumentBinaryObject(_relayData.LastPIH)
                }
            };
            references.Add(referencePIH);

            return references;
        }

        private AccountingSupplierParty CreateAccountingSupplierParty()
        {
            return new AccountingSupplierParty
            {
                Party = new Party
                {
                    PartyIdentification = new PartyIdentification
                    {
                        ID = new ID
                        {
                            SchemeID = _certInfo.IdentificationScheme,
                            Value = _certInfo.IdentificationID
                        }
                    },
                    PostalAddress = new PostalAddress
                    {
                        StreetName = _certInfo.StreetName,
                        BuildingNumber = _certInfo.BuildingNumber,
                        CitySubdivisionName = _certInfo.CitySubdivisionName,
                        CityName = _certInfo.CityName,
                        PostalZone = _certInfo.PostalZone,
                        Country = new Country
                        {
                            IdentificationCode = _certInfo.CountryIdentificationCode
                        }
                    },
                    PartyTaxScheme = new PartyTaxScheme
                    {
                        CompanyID = _certInfo.EnvironmentType == EnvironmentType.NonProduction ? "5900017383" : _certInfo.CompanyID,
                        TaxScheme = new TaxScheme
                        {
                            ID = new ID(_certInfo.TaxSchemeID)
                        }
                    },
                    PartyLegalEntity = new PartyLegalEntity
                    {
                        RegistrationName = _certInfo.RegistrationName
                    }
                }
            };
        }

        private AccountingCustomerParty CreateAccountingCustomerParty()
        {
            PartyTaxInfo partyInfo = _relayData.PartyInfo;

            return new AccountingCustomerParty
            {
                Party = new Party
                {
                    PostalAddress = new PostalAddress
                    {
                        StreetName = partyInfo.StreetName,
                        BuildingNumber = partyInfo.BuildingNumber,
                        CitySubdivisionName = partyInfo.CitySubdivisionName,
                        CityName = partyInfo.CityName,
                        PostalZone = partyInfo.PostalZone,
                        Country = new Country
                        {
                            IdentificationCode = partyInfo.CountryIdentificationCode
                        }
                    },
                    PartyTaxScheme = new PartyTaxScheme
                    {
                        CompanyID = partyInfo.CompanyID,
                        TaxScheme = new TaxScheme
                        {
                            ID = new ID(partyInfo.TaxSchemeID)
                        }
                    },
                    PartyLegalEntity = new PartyLegalEntity
                    {
                        RegistrationName = partyInfo.RegistrationName
                    }
                }

            };
        }

        private PaymentMeans CreatePaymentMeans()
        {
            var paymentMeansCode = 30;
            string paymentMeans = JsonParser.FindStringByGuid(_relayData.InvoiceJson, ManagerCustomField.PaymentMeansCodeGuid, "RefInvoice");
            string instructionNote = JsonParser.FindStringByGuid(_relayData.InvoiceJson, ManagerCustomField.InstructionNoteGuid, "RefInvoice");

            if (paymentMeans != null)
            {
                var parts = paymentMeans.Split('|');
                if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out int paymentCode))
                {
                    paymentMeansCode = paymentCode;
                }
            }

            return new PaymentMeans()
            {
                PaymentMeansCode = paymentMeansCode.ToString(),
                InstructionNote = instructionNote,
            };
        }

        private List<InvoiceLine> CreateInvoiceLines()
        {
            List<Line> lines = _managerInvoice.Lines;

            bool amountsIncludeTax = _managerInvoice.AmountsIncludeTax;
            bool hasDiscount = _managerInvoice.Discount;

            List<InvoiceLine> invoiceLines = new();
            int i = 0;

            foreach (var line in lines)
            {
                double percent = (line.TaxCode?.Rate ?? 0) / 100;
                double invoicedQuantity = Math.Round((line.Qty != 0 ? line.Qty : 1), 4);
                double discount = line.DiscountAmount * (hasDiscount ? 1 : 0);
                double unitPrice = ((line.UnitPrice * invoicedQuantity) - discount) / invoicedQuantity;
                double priceAmount = Math.Round(amountsIncludeTax ? (unitPrice / (1 + percent)) : unitPrice, 4);
                double lineExtensionAmount = Math.Round(invoicedQuantity * priceAmount, 2);
                double taxAmount = Math.Round(lineExtensionAmount * percent, 2);

                InvoiceLine invoiceLine = new();

                if (line.Item != null)
                {
                    invoiceLine.ID = new ID((++i).ToString());
                    invoiceLine.InvoicedQuantity = new InvoicedQuantity(line.Item.UnitName ?? "", invoicedQuantity);
                    invoiceLine.Item = new Zatca.eInvoice.Models.Item
                    {
                        Name = line.Item.ItemName ?? line.Item.Name,
                    };
                    invoiceLine.LineExtensionAmount = new Amount(_invoiceCurrencyCode, lineExtensionAmount);
                    invoiceLine.Price = new Price
                    {
                        PriceAmount = new Amount(_invoiceCurrencyCode, priceAmount),
                    };

                    VATInfo vatInfo = new VATInfo("S", null, null, null);

                    double rate = line.TaxCode?.Rate ?? 0;
                    if (rate == 0)
                    {
                        string itemTaxCategoryID = line.Item.CustomFields2.Strings[ManagerCustomField.ItemTaxCategoryGuid];
                        vatInfo = GetVATInfo(itemTaxCategoryID);
                    }

                    invoiceLine.Item.ClassifiedTaxCategory = new ClassifiedTaxCategory
                    {
                        Percent = rate,
                        ID = new ID("UN/ECE 5305", "6", vatInfo.CategoryID),
                        TaxScheme = new TaxScheme
                        {
                            ID = new ID("UN/ECE 5153", "6", "VAT")
                        }
                    };

                    invoiceLine.TaxTotal = new TaxTotal
                    {
                        TaxAmount = new Amount(_invoiceCurrencyCode, taxAmount),
                        RoundingAmount = new Amount(_invoiceCurrencyCode, lineExtensionAmount + taxAmount)
                    };
                }
                else
                {
                    //Prepaid Amount
                    invoiceLine.ID = new ID((++i).ToString());
                    invoiceLine.Price = new Price
                    {
                        PriceAmount = new Amount(_invoiceCurrencyCode, priceAmount),
                    };
                }

                invoiceLines.Add(invoiceLine);
            }

            return invoiceLines;
        }

        //private List<TaxTotal> CalculateTaxTotals()
        //{
        //    List<Line> lines = _managerInvoice.Lines;
        //    bool amountsIncludeTax = _managerInvoice.AmountsIncludeTax;
        //    double exchangeRate = _managerInvoice.ExchangeRate == 0 ? 1 : _managerInvoice.ExchangeRate;

        //    List<TaxTotal> taxTotals = new();
        //    double totalTaxAmount = 0;

        //    // Dictionary untuk menjumlahkan TaxableAmount, TaxAmount, dan informasi lainnya berdasarkan CategoryID + ExemptReasonCode
        //    Dictionary<string, (double taxableAmount, double taxAmount, double rate, string exemptReasonCode, string exemptReason)> taxSummaryByCategory = new();

        //    // Loop pertama: menjumlahkan nilai-nilai berdasarkan CategoryID + ExemptReasonCode
        //    foreach (var line in lines)
        //    {
        //        if (line.TaxCode != null)
        //        {
        //            double percent = (line.TaxCode?.Rate ?? 0) / 100;
        //            double invoicedQuantity = Math.Round((line.Qty != 0 ? line.Qty : 1), 4);
        //            double discount = line.DiscountAmount * (_managerInvoice.Discount ? 1 : 0);
        //            double unitPrice = ((line.UnitPrice * invoicedQuantity) - discount) / invoicedQuantity;
        //            double priceAmount = Math.Round(amountsIncludeTax ? (unitPrice / (1 + percent)) : unitPrice, 4);
        //            double lineExtensionAmount = Math.Round(invoicedQuantity * priceAmount, 2);
        //            double taxAmount = Math.Round(lineExtensionAmount * percent, 2);

        //            totalTaxAmount += taxAmount;

        //            VATInfo vatInfo = new VATInfo("S", null, null, null);
        //            double rate = line.TaxCode?.Rate ?? 0;

        //            if (rate == 0)
        //            {
        //                string itemTaxCategoryID = line.Item.CustomFields2.Strings[ManagerCustomField.ItemTaxCategoryGuid];
        //                vatInfo = GetVATInfo(itemTaxCategoryID);
        //            }

        //            string categoryID = vatInfo.CategoryID;
        //            string exemptReasonCode = rate == 0 ? vatInfo.ExemptReasonCode : "";
        //            string key = $"{categoryID}-{exemptReasonCode}";  // Gabungkan CategoryID dan ExemptReasonCode sebagai kunci

        //            // Tambahkan nilai ke dictionary sesuai CategoryID + ExemptReasonCode
        //            if (!taxSummaryByCategory.ContainsKey(key))
        //            {
        //                taxSummaryByCategory[key] = (0, 0, rate, exemptReasonCode, vatInfo.ExemptReason);
        //            }

        //            // Tambah nilai ke taxableAmount dan taxAmount untuk CategoryID dan ExemptReasonCode yang sama
        //            var currentSummary = taxSummaryByCategory[key];
        //            taxSummaryByCategory[key] = (currentSummary.taxableAmount + lineExtensionAmount,
        //                                         currentSummary.taxAmount + taxAmount,
        //                                         rate,
        //                                         exemptReasonCode,
        //                                         vatInfo.ExemptReason);
        //        }
        //    }

        //    // Tax total tanpa subtotals (dengan nilai pajak yang dikonversi)
        //    TaxTotal taxTotalWithoutSubtotals = new()
        //    {
        //        TaxAmount = new Amount(_taxCurrencyCode, totalTaxAmount * exchangeRate)
        //    };
        //    taxTotals.Add(taxTotalWithoutSubtotals);

        //    // Loop kedua: membuat TaxSubtotal berdasarkan hasil dari dictionary
        //    List<TaxSubtotal> taxSubtotals = new();

        //    foreach (var kvp in taxSummaryByCategory)
        //    {
        //        string categoryID = kvp.Key.Split('-')[0]; // Ambil CategoryID dari kunci
        //        string exemptReasonCode = kvp.Value.exemptReasonCode;
        //        double taxableAmount = kvp.Value.taxableAmount;
        //        double taxAmount = kvp.Value.taxAmount;
        //        double rate = kvp.Value.rate;
        //        string exemptReason = kvp.Value.exemptReason;

        //        TaxSubtotal taxSubtotal = new()
        //        {
        //            TaxableAmount = new Amount(_invoiceCurrencyCode, taxableAmount),
        //            TaxAmount = new Amount(_invoiceCurrencyCode, taxAmount),
        //            TaxCategory = new TaxCategory
        //            {
        //                Percent = rate,
        //                ID = new ID("UN/ECE 5305", "6", categoryID),
        //                TaxExemptionReasonCode = rate == 0 ? exemptReasonCode : null,  // Menambahkan TaxExemptionReasonCode
        //                TaxExemptionReason = rate == 0 ? exemptReason : null,          // Menambahkan TaxExemptionReason
        //                TaxScheme = new TaxScheme
        //                {
        //                    ID = new ID("UN/ECE 5153", "6", "VAT")
        //                }
        //            }
        //        };

        //        taxSubtotals.Add(taxSubtotal);
        //    }

        //    // Tax total dengan subtotals yang sudah digabungkan
        //    TaxTotal taxTotalWithSubtotals = new()
        //    {
        //        TaxAmount = new Amount(_invoiceCurrencyCode, totalTaxAmount),
        //        TaxSubtotal = taxSubtotals.ToArray()
        //    };

        //    taxTotals.Add(taxTotalWithSubtotals);

        //    return taxTotals;
        //}

        private List<TaxTotal> CalculateTaxTotals()
        {
            List<Line> lines = _managerInvoice.Lines;
            bool amountsIncludeTax = _managerInvoice.AmountsIncludeTax;
            bool hasDiscount = _managerInvoice.Discount;

            double exchangeRate = _managerInvoice.ExchangeRate == 0 ? 1 : _managerInvoice.ExchangeRate;

            List<TaxTotal> taxTotals = new();
            double totalTaxAmount = 0;

            Dictionary<string, (double taxableAmount, double taxAmount, double rate, string exemptReasonCode, string exemptReason)> taxSummaryByCategory = new();

            foreach (var line in lines)
            {
                if (line.TaxCode != null)
                {
                    LineValue lv = new LineValue(line, hasDiscount, amountsIncludeTax);

                    totalTaxAmount += lv.XmlTaxAmount;

                    VATInfo vatInfo = new VATInfo("S", null, null, null);
                    double rate = line.TaxCode?.Rate ?? 0;

                    if (rate == 0)
                    {
                        string itemTaxCategoryID = line.Item.CustomFields2.Strings[ManagerCustomField.ItemTaxCategoryGuid];
                        vatInfo = GetVATInfo(itemTaxCategoryID);
                    }

                    string categoryID = vatInfo.CategoryID;
                    string exemptReasonCode = rate == 0 ? vatInfo.ExemptReasonCode : "";
                    string key = $"{categoryID}-{exemptReasonCode}";

                    if (!taxSummaryByCategory.TryGetValue(key, out var currentSummary))
                    {
                        currentSummary = (0, 0, rate, exemptReasonCode, vatInfo.ExemptReason);
                    }

                    taxSummaryByCategory[key] = (currentSummary.taxableAmount + lv.XmlTaxableAmount,
                                                 currentSummary.taxAmount + lv.XmlTaxAmount,
                                                 rate,
                                                 exemptReasonCode,
                                                 vatInfo.ExemptReason);

                }
            }

            TaxTotal taxTotalWithoutSubtotals = new()
            {
                TaxAmount = new Amount(_taxCurrencyCode, totalTaxAmount * exchangeRate)
            };
            taxTotals.Add(taxTotalWithoutSubtotals);

            List<TaxSubtotal> taxSubtotals = new();

            foreach (var kvp in taxSummaryByCategory)
            {

                double taxableAmount = kvp.Value.taxableAmount;
                double taxAmount = kvp.Value.taxAmount;

                string categoryID = kvp.Key.Split('-')[0];
                double rate = kvp.Value.rate;
                string exemptReasonCode = kvp.Value.exemptReasonCode;
                string exemptReason = kvp.Value.exemptReason;

                TaxSubtotal taxSubtotal = new()
                {
                    TaxableAmount = new Amount(_invoiceCurrencyCode, taxableAmount),
                    TaxAmount = new Amount(_invoiceCurrencyCode, taxAmount),
                    TaxCategory = new TaxCategory
                    {
                        Percent = rate,
                        ID = new ID("UN/ECE 5305", "6", categoryID),
                        TaxExemptionReasonCode = rate == 0 ? exemptReasonCode : null,
                        TaxExemptionReason = rate == 0 ? exemptReason : null,
                        TaxScheme = new TaxScheme
                        {
                            ID = new ID("UN/ECE 5153", "6", "VAT")
                        }
                    }
                };

                taxSubtotals.Add(taxSubtotal);
            }


            TaxTotal taxTotalWithSubtotals = new()
            {
                TaxAmount = new Amount(_invoiceCurrencyCode, totalTaxAmount),
                TaxSubtotal = taxSubtotals.ToArray()
            };

            taxTotals.Add(taxTotalWithSubtotals);

            return taxTotals;
        }

        private LegalMonetaryTotal CalculateLegalMonetaryTotal()
        {
            List<Line> lines = _managerInvoice.Lines;
            bool amountsIncludeTax = _managerInvoice.AmountsIncludeTax;

            double sumLineExtensionAmount = 0;
            double sumTaxExclusiveAmount = 0;
            double sumTaxInclusiveAmount = 0;
            double sumAllowanceTotalAmount = 0;

            foreach (var line in lines)
            {
                double percent = (line.TaxCode?.Rate ?? 0) / 100;
                double invoicedQuantity = Math.Round((line.Qty != 0 ? line.Qty : 1), 4);
                double discount = line.DiscountAmount * (_managerInvoice.Discount ? 1 : 0);
                double unitPrice = ((line.UnitPrice * invoicedQuantity) - discount) / invoicedQuantity;
                double priceAmount = Math.Round(amountsIncludeTax ? (unitPrice / (1 + percent)) : unitPrice, 4);
                double lineExtensionAmount = Math.Round(invoicedQuantity * priceAmount, 2);
                double taxAmount = Math.Round(lineExtensionAmount * percent, 2);

                sumLineExtensionAmount += lineExtensionAmount;
                sumTaxExclusiveAmount += lineExtensionAmount;
                sumTaxInclusiveAmount += lineExtensionAmount + taxAmount;
                //sumAllowanceTotalAmount += discount;
            }

            return new LegalMonetaryTotal
            {
                LineExtensionAmount = new Amount(_invoiceCurrencyCode, sumLineExtensionAmount),
                TaxExclusiveAmount = new Amount(_invoiceCurrencyCode, sumTaxExclusiveAmount),
                TaxInclusiveAmount = new Amount(_invoiceCurrencyCode, sumTaxInclusiveAmount),
                AllowanceTotalAmount = new Amount(_invoiceCurrencyCode, sumAllowanceTotalAmount),
                //ChargeTotalAmount = new Amount(_invoiceCurrencyCode, 0),
                PrepaidAmount = new Amount(_invoiceCurrencyCode, 0),
                //PayableRoundingAmount = new Amount(_invoiceCurrencyCode,0),
                PayableAmount = new Amount(_invoiceCurrencyCode, sumTaxInclusiveAmount)
            };
        }
    }
}
