using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Quartz;
using Quartz.Util;
using Rock;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;

namespace com.bricksandmortarstudio.CrosspointNow.Jobs
{
    [LavaCommandsField( "Enabled Lava Commands", "The Lava commands that should be enabled for messages sent.", false, order: 2 )]
    [GroupField("Root Group", "The root group", order:4) ]
    [IntegerField("Look Ahead",
         "The number of days ahead a schedule should be considered for emailing. NOTE: If this job runs multiple times during this interval group members will receive multiple emails"
     , order: 6)]
    [BooleanField("Save Communications", "Whether a communication record should be saved for emails sent via this job",
         key: "IsSaved", order: 7)]
    [BooleanField("Ignore Group Locations Schedules",
         "If selected group location schedules won't be included when calculating scheduled groups", true,
         key: "IgnoreGroupLocations", order: 5)]
    [DefinedValueField(Rock.SystemGuid.DefinedType.COMMUNICATION_SMS_FROM, "From", "The phone number that should be used to send messages, also represents the phone number that will receive replies", order: 0)]
    [MemoField("Message", "The message to be sent", true, numberOfRows:5, order: 1)]
    public class SendGroupReminderSms : IJob
    {
        public void Execute(IJobExecutionContext context)
        {
            var dataMap = context.JobDetail.JobDataMap;
            
            var enabledLavaCommands = dataMap.GetString("EnabledLavaCommands");
            var ignoreGroupLocations = dataMap.GetBooleanValue("IgnoreGroupLocations");
            var rootGroupGuid = dataMap.GetString("RootGroup");
            var fromGuid = dataMap.GetString( "From" );
            var message = dataMap.GetString("Message");
            var isSaved = dataMap.GetBooleanFromString( "IsSaved" );

            var lookAhead = dataMap.GetString("LookAhead");
            var addDays = Convert.ToDouble(lookAhead.AsInteger());
            var cutOff = RockDateTime.Now.AddDays(addDays);

            if (rootGroupGuid.IsNullOrWhiteSpace() || fromGuid.IsNullOrWhiteSpace() || message.IsNullOrWhiteSpace() )
            {
                return;
            }

            var rockContext = new RockContext();
            var groupService = new GroupService(rockContext);
            var rootGroup = groupService.Get(rootGroupGuid.AsGuid());
            
            var from = DefinedValueCache.Read(fromGuid);
            var personService = new PersonAliasService(rockContext);
            var sender = personService.Get(from.AttributeValues["ResponseRecipient"].Value.AsGuid()).Person;

            if (sender == null)
            {
                throw new Exception("Unable to fetch sender");   
            }

            // Find child groups where the next scheduled date is within the cutoff period
            IEnumerable <Group> descendentGroups;

            if (!ignoreGroupLocations)
            {
                descendentGroups = rootGroup.Groups
                    .Where(
                        g =>
                            (g.Schedule != null && g.Schedule.NextStartDateTime != null &&
                             g.Schedule.NextStartDateTime < cutOff) ||
                            (g.GroupLocations != null &&
                             g.GroupLocations.Any(gl => gl.Schedules.Any(s => s.NextStartDateTime < cutOff))))
                    .ToList();
            }
            else
            {
                descendentGroups = rootGroup.Groups
                    .Where(
                        g =>
                            g.Schedule != null && g.Schedule.NextStartDateTime != null &&
                            g.Schedule.NextStartDateTime < cutOff)
                    .ToList();
            }


            if (descendentGroups.Any())
            {
                int messagedCount = 0;

                var recipients = new List<CommunicationRecipient>();
                foreach (var group in descendentGroups)
                {

                    var groupMembers = group.Members
                        .Where(m =>
                            m.GroupMemberStatus == GroupMemberStatus.Active
                            && m.Person.PhoneNumbers.Any(pn => pn.IsMessagingEnabled))
                        .ToList();

                    if (groupMembers.Any())
                    {
                        //Schedule to merge for lava. Pick the group schedule as a default and then the soonest nextstartdatetime otherwise
                        var selectedSchedule = group.Schedule ??
                                               group.GroupLocations.OrderBy(
                                                       gl => gl.Schedules.FirstOrDefault().NextStartDateTime)
                                                   .FirstOrDefault(gl => gl.Schedules.Any())
                                                   .Schedules.FirstOrDefault();

                        foreach (var groupMember in groupMembers)
                        {
                            if (groupMember.Person.PrimaryAliasId.HasValue)
                            {
                                var recipient = new CommunicationRecipient();
                                recipient.PersonAliasId = groupMember.Person.PrimaryAliasId.Value;
                                recipient.AdditionalMergeValues = new Dictionary<string, object>
                            {
                                {"Group", group},
                                {"GroupMember", groupMember},
                                {"Person", groupMember.Person},
                                {"Schedule", selectedSchedule}
                            };
                                recipient.AdditionalMergeValuesJson = String.Empty;
                                recipients.Add( recipient );
                            }
                            messagedCount++;
                        }

                        var communicationService = new CommunicationService(rockContext);

                        var communication = new Communication();
                        communication.SenderPersonAliasId = sender.PrimaryAliasId;

                        communication.Subject = communication.Subject;
                        communication.IsBulkCommunication = false;
                        communication.MediumEntityTypeId = EntityTypeCache.Read( "Rock.Communication.Medium.Sms" ).Id; 
                        communication.EnabledLavaCommands = enabledLavaCommands;

                        communication.FutureSendDateTime = null;
                        communication.Status = CommunicationStatus.Approved;
                        communication.ReviewedDateTime = RockDateTime.Now;
                        communication.ReviewerPersonAliasId = sender.PrimaryAliasId;
                        communication.MediumData = new Dictionary<string, string>();
                        communication.SetMediumDataValue("Message", message);
                        communication.SetMediumDataValue( "FromValue", from.Id.ToString() );
                        communication.SetMediumDataValue("Subject", "From: " + sender.FullName);
                        communication.Recipients = recipients;

                        communicationService.Add(communication);
                        rockContext.SaveChanges();

                        var medium = communication.Medium;
                        if ( medium != null && medium.IsActive )
                        {
                            medium.Send( communication );
                        }

                        if (!isSaved)
                        {
                            communicationService.Delete( communication );
                            rockContext.SaveChanges();
                        }
                    }
                }

                context.Result =
                    string.Format(
                        "{0} group members were attempted to be messaged in the following group".PluralizeIf(descendentGroups.Count() > 1) + " " +
                        string.Join(", ", descendentGroups.AsEnumerable()), messagedCount);
            }

            else
            {
                context.Result = "No group members messaged.";
            }
        }
//
//        private string BuildAdditionalMergeValues(CommunicationRecipient recipient)
//        {
//            var jArray = new JArray();
//            foreach (var additionalMergeValue in recipient.AdditionalMergeValues)
//            {
//                
//            }
//        }
    }
}
