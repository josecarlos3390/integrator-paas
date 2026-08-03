// ============================================================================
// SAP Business One Add-on — PostTransaction Handler
// ============================================================================
// This code runs INSIDE the SAP B1 process as a DI API Add-on.
// It connects to SAP HANA (schema INTEGRATION_BUS) and inserts a minimal
// record into OUTBOX_EVENTS every time an A/R Invoice is created.
//
// Requirements:
//   - References: SAPbouiCOM.dll, SAPbobsCOM.dll
//   - NuGet: Sap.Data.Hana.Core (or System.Data.Odbc as fallback)
//   - The Add-on must be registered in SAP B1 to listen for the
//     et_FORM_DATA_ADD event on the Invoices form or use the global
//     Company.GetBusinessObjectInfo / PostTransaction event.
//
// NOTE: PostTransaction fires SYNCHRONOUSLY after the SAP Commit.
//       NEVER perform HTTP calls here because it blocks the SAP UI.
// ============================================================================

using System;
using System.Data;
using System.Data.Odbc;
using SAPbouiCOM;
using SAPbobsCOM;

namespace SapIntegrationAddon;

/// <summary>
/// SAP B1 PostTransaction event handler.
/// Captures A/R Invoice transactions (objectType = 13, transType = et_ADD)
/// and writes a domain event into the HANA outbox table.
/// </summary>
public class PostTransactionHandler
{
    private readonly Application _sboApplication;
    private readonly Company _company;
    private readonly string _hanaConnectionString;
    private readonly string _tenantId;

    public PostTransactionHandler(
        Application sboApplication,
        Company company,
        string hanaConnectionString,
        string tenantId)
    {
        _sboApplication = sboApplication;
        _company = company;
        _hanaConnectionString = hanaConnectionString;
        _tenantId = tenantId;
    }

    /// <summary>
    /// Registers the handler on the SAP B1 global PostTransaction event.
    /// </summary>
    public void Subscribe()
    {
        // In the SAP B1 SDK, PostTransaction is not a direct Application event.
        // The recommended pattern is to use the et_FORM_DATA_ADD / et_FORM_DATA_UPDATE
        // event of form 133 (Invoices) or intercept via DI Events.
        // Below is the pattern using the form's ItemEvent.
        _sboApplication.ItemEvent += OnItemEvent;
    }

    private void OnItemEvent(string formUid, ref ItemEvent pVal, out bool bubbleEvent)
    {
        bubbleEvent = true;

        try
        {
            // Filter: Invoices form (133), validation event after add,
            // successful action (ActionSuccess = true)
            if (pVal.FormTypeEx == "133"
                && pVal.EventType == BoEventTypes.et_FORM_DATA_ADD
                && pVal.ActionSuccess
                && pVal.BeforeAction == false)
            {
                // Get DocEntry from the form
                var docEntry = GetDocEntryFromForm(pVal.FormUID);
                if (docEntry > 0)
                {
                    InsertOutboxEvent(docEntry, "InvoiceCreated");
                }
            }

            // Future events:
            // FormTypeEx "134" (Business Partners) -> objectType 2
            // FormTypeEx "139" (Sales Orders)     -> objectType 17
            // FormTypeEx "143" (Goods Receipt PO) -> objectType 59
        }
        catch (Exception ex)
        {
            // Never rethrow exceptions in SAP B1 event handlers.
            // Silent logging to avoid disrupting the user experience.
            _sboApplication.StatusBar.SetSystemMessage(
                $"Integration outbox insert failed: {ex.Message}",
                BoMessageTime.bmt_Short,
                BoStatusBarMessageType.smt_Warning);
        }
    }

    /// <summary>
    /// Extracts the DocEntry from the active invoices form.
    /// </summary>
    private int GetDocEntryFromForm(string formUid)
    {
        var form = _sboApplication.Forms.Item(formUid);
        var dbDataSource = form.DataSources.DBDataSources.Item("OINV");
        // DocEntry is the auto-generated PK by SAP after the Add
        var docEntryStr = dbDataSource.GetValue("DocEntry", 0);
        return int.TryParse(docEntryStr, out var de) ? de : 0;
    }

    /// <summary>
    /// Inserts a record into INTEGRATION_BUS.OUTBOX_EVENTS in HANA.
    /// </summary>
    private void InsertOutboxEvent(int docEntry, string eventType)
    {
        const string sql = @"
            INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
                (ID, TENANT_ID, EVENT_TYPE, AGGREGATE_ID, OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, IS_DEAD_LETTER)
            VALUES
                (SYSUUID(), ?, ?, ?, CURRENT_TIMESTAMP, NULL, 0, 0);
        ";

        using var connection = new OdbcConnection(_hanaConnectionString);
        connection.Open();

        using var command = new OdbcCommand(sql, connection);
        command.Parameters.Add("?", OdbcType.NVarChar, 64).Value = _tenantId;
        command.Parameters.Add("?", OdbcType.NVarChar, 128).Value = eventType;
        command.Parameters.Add("?", OdbcType.NVarChar, 128).Value = docEntry.ToString();

        var rowsAffected = command.ExecuteNonQuery();

        if (rowsAffected > 0)
        {
            _sboApplication.StatusBar.SetSystemMessage(
                $"Integration event queued: {eventType} #{docEntry}",
                BoMessageTime.bmt_Short,
                BoStatusBarMessageType.smt_Success);
        }
    }
}

// ============================================================================
// Add-on initialization (simplified example)
// ============================================================================
public class Program
{
    public static void Main()
    {
        var sboApp = new Application();
        var company = new Company();

        // ... connect to SAP B1 via DI API ...

        var handler = new PostTransactionHandler(
            sboApp,
            company,
            hanaConnectionString: "Driver={HDBODBC};SERVERNODE=sapserver:39017;UID=USER;PWD=PASS;CS=INTEGRATION_BUS;",
            tenantId: "tenant-001");

        handler.Subscribe();

        // ... start the Add-on message pump ...
    }
}
