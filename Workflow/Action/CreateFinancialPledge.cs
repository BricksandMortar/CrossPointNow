using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using Rock;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;
using Rock.Workflow;

namespace com.bricksandmortarstudio.CrosspointNow.Workflow.Action
{
    [ActionCategory( "Finance" )]
    [Description( "Adds a financial pledge." )]
    [Export( typeof( ActionComponent ) )]
    [ExportMetadata( "ComponentName", "Financial Pledge Add" )]
    [WorkflowAttribute( "Person Attribute", "The Person attribute that contains the person that financial pledge should be created for.", true, "", "", 0, null,
       new string[] { "Rock.Field.Types.PersonFieldType" } )]
    [WorkflowAttribute( "Group Attribute", "The attribute that contains the group the financial pledge should be associated with.", false, "", "", 1, null,
       new string[] { "Rock.Field.Types.GroupFieldType" } )]
    [WorkflowAttribute( "Financial Account Attribute", "The attribute that contains the financial account the pledge belongs to.", true, "", "", 2, null,
       new string[] { "Rock.Field.Types.AccountFieldType" } )]
    [WorkflowAttribute( "Amount Attribute", "The attribute that contains the currency amount that the pledge is for", true, "", "", 4, null,
       new string[] { "Rock.Field.Types.CurrencyFieldType" } )]
    [WorkflowAttribute( "Start Date Attribute", "The attribute that contains the start date for the pledge", true, "", "", 5, null,
       new string[] { "Rock.Field.Types.DateFieldType" } )]
    [WorkflowAttribute( "End Date Attribute", "The attribute that contains the start date for the pledge", true, "", "", 6, null,
       new string[] { "Rock.Field.Types.DateFieldType" } )]
    [WorkflowAttribute( "Pledge Frequency Attribute", "The attribute that contains frequency of the pledge", true, "", "", 7, null,
       new string[] { "Rock.Field.Types.DefinedValueFieldType" } )]

    class CreateFinancialPledge : ActionComponent
    {
        public override bool Execute(RockContext rockContext, WorkflowAction action, object entity, out List<string> errorMessages)
        {
            errorMessages = new List<string>();

            // Get the person
            PersonAlias personAlias = null;
            Guid personAliasGuid = action.GetWorklowAttributeValue( GetAttributeValue( action, "PersonAttribute" ).AsGuid() ).AsGuid();
            personAlias = new PersonAliasService( rockContext ).Get( personAliasGuid );
            if ( personAlias == null )
            {
                errorMessages.Add( "Invalid Person Attribute or Value!" );
                return false;
            }


            int? campusId = null;
            Guid? campusAttributeGuid = GetAttributeValue( action, "CampusAttribute" ).AsGuidOrNull();
            if ( campusAttributeGuid.HasValue )
            {
                Guid? campusGuid = action.GetWorklowAttributeValue( campusAttributeGuid.Value ).AsGuidOrNull();
                if ( campusGuid.HasValue )
                {
                    var campus = CampusCache.Read( campusGuid.Value );
                    if ( campus != null )
                    {
                        campusId = campus.Id;
                    }
                }
            }

            // Get the Group
            Group group = null;
            Guid? groupAttributeGuid = GetAttributeValue(action, "GroupAttribute").AsGuidOrNull();
            if (groupAttributeGuid.HasValue)
            {
                Guid? groupGuid = action.GetWorklowAttributeValue(groupAttributeGuid.Value).AsGuid();
                if (groupGuid.HasValue)
                {
                    group = new GroupService( rockContext ).Get( groupGuid.Value );
                }
            }
            

            // Get Financial Account
            FinancialAccount financialAccount = null;
            Guid financialAccountGuid = action.GetWorklowAttributeValue( GetAttributeValue( action, "FinancialAccountAttribute" ).AsGuid()).AsGuid();
            financialAccount = new FinancialAccountService(rockContext).Get(financialAccountGuid);
            if (financialAccount == null)
            {
                errorMessages.Add( "Invalid Financial Account Attribute or Value!" );
                return false;
            }

            // Get Frequency
            int? pledgeFreuqencyValueId = null;
            Guid pledgeFrequencyDefinedValueAttributeGuid = GetAttributeValue( action, "PledgeFrequencyAttribute" ).AsGuid();
            var pledgeFrequencyDefinedValue = DefinedValueCache.Read( action.GetWorklowAttributeValue( pledgeFrequencyDefinedValueAttributeGuid ).AsGuid());
            if (pledgeFrequencyDefinedValue == null || pledgeFrequencyDefinedValue.DefinedType.Guid != Rock.SystemGuid.DefinedType.FINANCIAL_FREQUENCY.AsGuid())
            {
                errorMessages.Add( "Invalid Pledge Frequency Attribute or Value!" );
                return false;
            }
            pledgeFreuqencyValueId = pledgeFrequencyDefinedValue.Id;

            // ReSharper disable once PossibleInvalidOperationException
            var startDate = action.GetWorklowAttributeValue(GetAttributeValue(action, "StartDateAttribute").AsGuid()).AsDateTime().Value;
            // ReSharper disable once PossibleInvalidOperationException
            var endDate = action.GetWorklowAttributeValue( GetAttributeValue( action, "EndDateAttribute" ).AsGuid() ).AsDateTime().Value;
            decimal amount = action.GetWorklowAttributeValue(GetAttributeValue( action, "AmountAttribute" ).AsGuid()).AsDecimal();

            var financialPledgeService = new FinancialPledgeService( rockContext );

            var financialPledge = new FinancialPledge();
            financialPledge.PersonAliasId = personAlias.Id;
            financialPledge.AccountId = financialAccount.Id;
            financialPledge.StartDate = startDate;
            financialPledge.EndDate = endDate;
            financialPledge.GroupId = group.Id;
            financialPledge.PledgeFrequencyValueId = pledgeFreuqencyValueId;
            financialPledge.TotalAmount = amount;

            financialPledgeService.Add( financialPledge );
            rockContext.SaveChanges();

            return true;
        }
    }
}
