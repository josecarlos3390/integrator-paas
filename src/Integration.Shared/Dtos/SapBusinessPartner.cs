namespace Integration.Shared.Dtos;

/// <summary>
/// Complete SAP Business One Service Layer DTO for BusinessPartners.
/// Includes standard fields, UDFs and nested collections.
/// </summary>
public class SapBusinessPartner
{
    // Identification
    public string CardCode { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string CardForeignName { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public int? Series { get; set; }
    public int? GroupCode { get; set; }
    public string? AliasName { get; set; }

    // Direct Contact
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Cellular { get; set; }
    public string? Fax { get; set; }
    public string? EmailAddress { get; set; }
    public string? Website { get; set; }
    public string? ContactPerson { get; set; }
    public string? Password { get; set; }

    // Fiscal
    public string? FederalTaxID { get; set; }
    public string? VatGroup { get; set; }
    public string? VatIDNum { get; set; }
    public string? VATRegistrationNumber { get; set; }
    public string? UnifiedFederalTaxID { get; set; }
    public string? CompanyRegistrationNumber { get; set; }
    public string? VerificationNumber { get; set; }

    // Main Address
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? County { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? MailAddress { get; set; }
    public string? MailZipCode { get; set; }
    public string? MailCity { get; set; }
    public string? MailCounty { get; set; }
    public string? MailCountry { get; set; }

    // Financial
    public decimal? CreditLimit { get; set; }
    public decimal? CurrentAccountBalance { get; set; }
    public decimal? MaxCommitment { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? PayTermsGrpCode { get; set; }
    public int? PriceListNum { get; set; }
    public string? Currency { get; set; }
    public decimal? CommissionPercent { get; set; }
    public int? CommissionGroupCode { get; set; }
    public decimal? IntrestRatePercent { get; set; }
    public decimal? MinIntrest { get; set; }
    public string? DebitorAccount { get; set; }
    public string? DownPaymentInterimAccount { get; set; }
    public string? DownPaymentClearAct { get; set; }

    // Commercial
    public int? SalesPersonCode { get; set; }
    public string? FreeText { get; set; }
    public string? Notes { get; set; }
    public string? Valid { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string? ValidRemarks { get; set; }
    public string? Frozen { get; set; }
    public DateTime? FrozenFrom { get; set; }
    public DateTime? FrozenTo { get; set; }
    public string? FrozenRemarks { get; set; }
    public string? Block { get; set; }
    public string? BackOrder { get; set; }
    public string? PartialDelivery { get; set; }
    public string? BlockDunning { get; set; }
    public string? CollectionAuthorization { get; set; }
    public string? SinglePayment { get; set; }
    public string? PaymentBlock { get; set; }
    public int? PaymentBlockDescription { get; set; }
    public string? EndorsableChecksFromBP { get; set; }
    public string? AcceptsEndorsedChecks { get; set; }
    public string? BlockSendingMarketingContent { get; set; }

    // Status y metadata
    public string? Indicator { get; set; }
    public int? Priority { get; set; }
    public string? CompanyPrivate { get; set; }
    public int? LanguageCode { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateTime { get; set; }
    public int? OpenOpportunities { get; set; }
    public string? SubjectToWithholdingTax { get; set; }
    public string? DeferredTax { get; set; }
    public string? Equalization { get; set; }
    public string? AccrualCriteria { get; set; }
    public string? NoDiscounts { get; set; }
    public string? AutomaticPosting { get; set; }
    public string? ThresholdOverlook { get; set; }
    public string? SurchargeOverlook { get; set; }

    // Banking
    public string? DefaultBankCode { get; set; }
    public string? DefaultBranch { get; set; }
    public string? DefaultAccount { get; set; }
    public string? HouseBank { get; set; }
    public string? HouseBankCountry { get; set; }
    public string? HouseBankAccount { get; set; }
    public string? HouseBankIBAN { get; set; }
    public string? HouseBankBranch { get; set; }
    public string? IBAN { get; set; }
    public int? CreditCardCode { get; set; }
    public string? CreditCardNum { get; set; }
    public DateTime? CreditCardExpiration { get; set; }

    // Relationships
    public string? FatherCard { get; set; }
    public string? FatherType { get; set; }
    public string? Affiliate { get; set; }
    public string? LinkedBusinessPartner { get; set; }

    // EDoc / EDI
    public string? EDocGenerationType { get; set; }
    public string? EDocStreet { get; set; }
    public string? EDocStreetNumber { get; set; }
    public string? EDocBuildingNumber { get; set; }
    public string? EDocZipCode { get; set; }
    public string? EDocCity { get; set; }
    public string? EDocCountry { get; set; }
    public string? EDocDistrict { get; set; }
    public string? EDocRepresentativeFirstName { get; set; }
    public string? EDocRepresentativeSurname { get; set; }
    public string? EDocRepresentativeCompany { get; set; }
    public string? EDocRepresentativeFiscalCode { get; set; }
    public string? EDocRepresentativeAdditionalId { get; set; }
    public string? EDocPECAddress { get; set; }
    public string? IPACodeForPA { get; set; }

    // UDFs (User Defined Fields)
    public string? U_AjuUFV { get; set; }
    public string? U_Ajuste { get; set; }
    public string? U_AITBSN { get; set; }
    public string? U_ORIGIN { get; set; }
    public string? U_TOKEN { get; set; }
    public string? U_LBA_AITBSN { get; set; }
    public string? U_B_dni_type { get; set; }
    public string? U_B_compl { get; set; }
    public string? U_LBA_AJUUFV { get; set; }
    public string? U_LBA_AJUSTE { get; set; }
    public string? U_MONEDA { get; set; }
    public DateTime? U_FECHA_ANI { get; set; }

    // Nested Collections
    public List<SapBusinessPartnerAddress> BPAddresses { get; set; } = new();
    public List<SapContactEmployee> ContactEmployees { get; set; } = new();
    public List<SapBPBankAccount> BPBankAccounts { get; set; } = new();
    public List<SapBPFiscalTaxID> BPFiscalTaxIDCollection { get; set; } = new();
    public List<SapBPPaymentMethod> BPPaymentMethods { get; set; } = new();
}

public class SapBusinessPartnerAddress
{
    public string? AddressName { get; set; }
    public string? Street { get; set; }
    public string? Block { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? County { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? FederalTaxID { get; set; }
    public string? TaxCode { get; set; }
    public string? BuildingFloorRoom { get; set; }
    public string? AddressType { get; set; } // bo_BillTo, bo_ShipTo
    public string? AddressName2 { get; set; }
    public string? AddressName3 { get; set; }
    public string? TypeOfAddress { get; set; }
    public string? StreetNo { get; set; }
    public string? BPCode { get; set; }
    public int? RowNum { get; set; }
    public string? GlobalLocationNumber { get; set; }
    public string? Nationality { get; set; }
    public string? TaxOffice { get; set; }
    public string? GSTIN { get; set; }
    public string? GstType { get; set; }
    public string? U_LATITUDE { get; set; }
    public string? U_LONGITUDE { get; set; }
    public string? U_VISITED { get; set; }
    public string? U_MOBILE_ADDRESS { get; set; }
}

public class SapContactEmployee
{
    public int? InternalCode { get; set; }
    public string? Name { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Title { get; set; }
    public string? Position { get; set; }
    public string? Address { get; set; }
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? MobilePhone { get; set; }
    public string? Fax { get; set; }
    public string? E_Mail { get; set; }
    public string? Pager { get; set; }
    public string? Remarks1 { get; set; }
    public string? Remarks2 { get; set; }
    public string? Password { get; set; }
    public string? Active { get; set; }
    public string? CardCode { get; set; }
    public string? PlaceOfBirth { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Profession { get; set; }
    public string? CityOfBirth { get; set; }
    public string? EmailGroupCode { get; set; }
    public string? BlockSendingMarketingContent { get; set; }
    public List<SapContactEmployeeMarketingBlock> ContactEmployeeBlockSendingMarketingContents { get; set; } = new();
}

public class SapContactEmployeeMarketingBlock
{
    public int? ContactEmployeeAbsEntry { get; set; }
    public int? CommunicationMediaId { get; set; }
    public string? Choose { get; set; }
}

public class SapBPBankAccount
{
    public string? BPCode { get; set; }
    public int? InternalKey { get; set; }
    public string? BankCode { get; set; }
    public string? AccountNo { get; set; }
    public string? Branch { get; set; }
    public string? IBAN { get; set; }
    public string? Country { get; set; }
    public string? BICSwiftCode { get; set; }
    public string? AccountName { get; set; }
    public string? DefaultAccount { get; set; }
    public string? ControlKey { get; set; }
    public string? UserNo1 { get; set; }
    public string? UserNo2 { get; set; }
    public string? UserNo3 { get; set; }
    public string? UserNo4 { get; set; }
    public string? Street { get; set; }
    public string? Block { get; set; }
    public string? City { get; set; }
    public string? ZipCode { get; set; }
    public string? County { get; set; }
    public string? State { get; set; }
    public string? LogInstance { get; set; }
}

public class SapBPFiscalTaxID
{
    public string? BPCode { get; set; }
    public int? InternalKey { get; set; }
    public string? TaxId0 { get; set; }
    public string? TaxId1 { get; set; }
    public string? TaxId2 { get; set; }
    public string? TaxId3 { get; set; }
    public string? TaxId4 { get; set; }
    public string? TaxId5 { get; set; }
    public string? TaxId6 { get; set; }
    public string? TaxId7 { get; set; }
    public string? TaxId8 { get; set; }
    public string? TaxId9 { get; set; }
    public string? TaxId10 { get; set; }
    public string? TaxId11 { get; set; }
    public string? AddrType { get; set; }
}

public class SapBPPaymentMethod
{
    public string? BPCode { get; set; }
    public int? InternalKey { get; set; }
    public string? PaymentMethodCode { get; set; }
}
