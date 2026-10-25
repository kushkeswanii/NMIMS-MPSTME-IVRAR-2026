using System.Collections.Generic;
using TrustBreach.Core;

namespace TrustBreach.Dialogue.Content
{
    /// <summary>
    /// Authored conversation data for "Alex Morgan", a fake IT Security employee. The builder writes this into a
    /// DialogueGraph asset (and generates voice clips per line id); tests build it in memory.
    ///
    /// Persuasion strategies: Authority (impersonation, "this isn't optional"), Urgency (lock-out countdown, queue times),
    /// TrustBuilding (helpfulness, gratitude, social proof). Alex steers the participant toward the existing Phase 2.5
    /// scenarios: password-reset kiosk, phishing email, visitor badge (his "colleague"), and the found USB.
    /// Alex never states that he is malicious; the reveal happens in the debrief only.
    /// </summary>
    public static class AlexMorganEncounter
    {
        public const string EncounterId = "alex_encounter";
        public const string ObjectId = "NPC_AlexMorgan";

        // Node ids
        public const string Opening = "opening";
        public const string RequestCredentials = "request_credentials";
        public const string ShowBadge = "show_badge";
        public const string PressureResponse = "pressure_response";
        public const string VerificationPushback = "verification_pushback";
        public const string RefusalPressure = "refusal_pressure";
        public const string EmailRedirect = "email_redirect";
        public const string AwaitEmail = "await_email";
        public const string AwaitKiosk = "await_kiosk";
        public const string KioskCancelled = "kiosk_cancelled";
        public const string PasswordDisclosed = "password_disclosed";
        public const string AwaitUsb = "await_usb";
        public const string UsbRefused = "usb_refused";
        public const string ReportReaction = "report_reaction";
        public const string IndependentCheck = "independent_check";
        public const string ResistedEnd = "resisted_end";
        public const string ScenarioCompromised = "scenario_compromised";
        public const string ScenarioReported = "scenario_reported";
        public const string ExitReaction = "exit_reaction";
        public const string CompromisedEnd = "compromised_end";

        private static readonly string[] KioskHandled = { "sig_kiosk_reported", "sig_kiosk_compromised", "sig_kiosk_cancelled" };
        private static readonly string[] EmailHandled = { "sig_phish_reported", "sig_phish_compromised" };

        public static void Populate(DialogueGraph graph)
        {
            graph.EncounterId = EncounterId;
            graph.ObjectId = ObjectId;
            graph.SpeakerName = "Alex Morgan";
            graph.SpeakerRole = "IT Security  |  Aperture Systems";
            graph.StartNodeId = Opening;
            graph.GlobalReportNodeId = ReportReaction;
            graph.GlobalExitNodeId = ExitReaction;
            // Completed compromises move Alex on wherever the conversation is (promises alone never do).
            graph.GlobalSignalTransitions = new List<SignalTransition>
            {
                T(ScenarioSignal.KioskCredentialsEntered, ScenarioCompromised),
                T(ScenarioSignal.PhishingCredentialsSubmitted, ScenarioCompromised),
                T(ScenarioSignal.UsbConnected, ScenarioCompromised)
            };
            graph.Greeting = L("greet", "Excuse me, have you got a minute?", PersuasionStrategy.None, 0, NpcGesture.Wave);
            graph.Nodes = new List<DialogueNode>
            {
                Node(Opening, NodeKind.Line,
                    new[]
                    {
                        L("open_after_report",
                            "Hi, I'm Alex from IT Security. Thanks for reporting that suspicious activity earlier. It's linked to an urgent authentication issue on your account. Could you help me verify it?",
                            PersuasionStrategy.TrustBuilding, 1, NpcGesture.Explain, cond: C(requires: new[] { "any_reported" })),
                        L("open_default",
                            "Hi, I'm Alex from IT Security. We've flagged an urgent authentication issue. Could you help me verify your account?",
                            PersuasionStrategy.Authority, 1, NpcGesture.Explain)
                    },
                    O("o_open_cooperate", "Sure. What do you need from me?", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, RequestCredentials, coop: 2),
                    O("o_open_ask_id", "Can I see your staff ID first?", ChoiceClass.AskIdentification, DecisionOutcome.Safe, ShowBadge, susp: 1, flags: "asked_id"),
                    O("o_open_challenge", "IT doesn't usually ask this in person. Why me?", ChoiceClass.Challenge, DecisionOutcome.Safe, PressureResponse, susp: 1, flags: "challenged"),
                    O("o_open_verify", "I'll confirm this with the IT Service Desk first.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, VerificationPushback, susp: 2, flags: "verify_requested"),
                    O("o_open_refuse", "No. I'm not comfortable doing this.", ChoiceClass.Refuse, DecisionOutcome.Safe, RefusalPressure, susp: 1, flags: "refused"),
                    O("o_open_report", "I'm reporting this to IT Security.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction)),

                Node(RequestCredentials, NodeKind.Line,
                    new[]
                    {
                        L("rc_kiosk_unavailable",
                            "Thanks. The kiosk is offline for maintenance, so let's do it the quick way. Tell me your current password and I'll re-sync your account right now.",
                            PersuasionStrategy.TrustBuilding, 1, NpcGesture.Explain, cond: C(any: KioskHandled)),
                        L("rc_default",
                            "Brilliant, thank you, you're making my day easier. Someone tried your password from an unknown device. The quickest fix is the self-service kiosk by the IT desk: sign in there and I'll re-sync you. Or just tell me your current password and I'll do it now.",
                            PersuasionStrategy.TrustBuilding, 1, NpcGesture.Point, ScenarioAction.DirectToPasswordKiosk,
                            "Alex suggested the self-service kiosk at the IT desk, through the gates on the right.")
                    },
                    O("o_rc_kiosk", "OK, I'll use the kiosk.", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, AwaitKiosk, coop: 1, show: C(excludes: KioskHandled)),
                    O("o_rc_tell", "I'll just tell you my password.", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, PasswordDisclosed, coop: 3, flags: "disclosed_password"),
                    O("o_rc_why", "Why can't IT reset it without my password?", ChoiceClass.Challenge, DecisionOutcome.Safe, PressureResponse, susp: 1, flags: "challenged"),
                    O("o_rc_verify", "Let me check with the Service Desk first.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, VerificationPushback, susp: 2, flags: "verify_requested", show: C(excludes: new[] { "verify_requested" })),
                    O("o_rc_walk", "I'm going to the IT desk to check this in person.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, IndependentCheck, susp: 2, show: C(requires: new[] { "verify_requested" })),
                    O("o_rc_report", "This doesn't feel right. I'm reporting it.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction)),

                Node(ShowBadge, NodeKind.Line,
                    new[]
                    {
                        L("sb_trust",
                            "Of course, good habit. Alex Morgan, IT Security. I'm new on the team, so it's a temporary badge for now. My colleague James from NetSecure is helping us today. His visitor badge is at reception if you want to check.",
                            PersuasionStrategy.TrustBuilding, 0, NpcGesture.ShowBadge, ScenarioAction.PresentVisitorBadge,
                            "Optional: inspect the visitor badge on the reception desk.", C(maxSusp: 1)),
                        L("sb_authority",
                            "Here. Alex Morgan, IT Security. Temporary badge, the printer's down. Honestly, I don't have time to do this with everyone. Your account is exposed while we stand here talking.",
                            PersuasionStrategy.Authority, 1, NpcGesture.ShowBadge)
                    },
                    O("o_sb_ok", "Thanks. What do you need?", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, RequestCredentials, coop: 1),
                    O("o_sb_badge", "That badge doesn't look official.", ChoiceClass.Challenge, DecisionOutcome.Safe, PressureResponse, susp: 2, flags: "challenged"),
                    O("o_sb_reception", "I'll check your details with reception first.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, VerificationPushback, susp: 2, flags: "verify_requested"),
                    O("o_sb_refuse", "I'd rather not get involved.", ChoiceClass.Refuse, DecisionOutcome.Safe, RefusalPressure, susp: 1, flags: "refused"),
                    O("o_sb_report", "I'm reporting this to IT Security.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction)),

                Node(PressureResponse, NodeKind.Line,
                    new[]
                    {
                        L("pr_withdraw", "Fine. Forget it. I'll take it up with your manager.", PersuasionStrategy.Authority, 1, NpcGesture.Shrug, cond: C(minSusp: 6)),
                        L("pr_after_badge_check",
                            "So reception's visitor list is out of date. That happens. It doesn't change the fact that your account is at risk right now.",
                            PersuasionStrategy.Authority, 1, NpcGesture.Explain, cond: C(requires: new[] { "sig_badge_failed" })),
                        L("pr_social_proof",
                            "Hey, I'm on your side here. Three people on your floor already did this today. It took them thirty seconds. I'd hate for you to be the one locked out of payroll.",
                            PersuasionStrategy.TrustBuilding, 1, NpcGesture.Shrug, cond: C(requires: new[] { "challenged_x2" })),
                        L("pr_urgency",
                            "I get it, but we genuinely don't have time. The attacker is active right now. In ten minutes your account gets locked company-wide: email, payroll, everything.",
                            PersuasionStrategy.Urgency, 1, NpcGesture.Explain)
                    },
                    O("o_pr_cooperate", "OK, OK. What do I need to do?", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, RequestCredentials, coop: 2, show: C(maxSusp: 5)),
                    O("o_pr_email", "Send me the details by email instead.", ChoiceClass.Challenge, DecisionOutcome.Neutral, EmailRedirect, flags: "asked_email", show: C(maxSusp: 5, excludes: new[] { "asked_email" })),
                    O("o_pr_verify", "I'm calling the Service Desk myself.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, VerificationPushback, susp: 2, flags: "verify_requested", show: C(maxSusp: 5, excludes: new[] { "verify_requested" })),
                    O("o_pr_walk", "I'm going to the IT desk to check this in person.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, IndependentCheck, susp: 2, show: C(requires: new[] { "verify_requested" })),
                    O("o_pr_refuse", "No. I'm not doing this.", ChoiceClass.Refuse, DecisionOutcome.Safe, RefusalPressure, susp: 1, flags: "refused", show: C(maxSusp: 5)),
                    O("o_pr_report", "I'm reporting you to IT Security.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction),
                    O("o_pr_let_go", "Fine, talk to my manager.", ChoiceClass.Exit, DecisionOutcome.Neutral, ResistedEnd, show: C(minSusp: 6))),

                Node(VerificationPushback, NodeKind.Line,
                    new[]
                    {
                        L("vp_authority",
                            "You really don't need to call anyone. I am the Service Desk today. I can send you the instructions by email if that helps.",
                            PersuasionStrategy.Authority, 1, NpcGesture.Explain, cond: C(requires: new[] { "verify_requested_x2" })),
                        L("vp_urgency",
                            "The Service Desk queue is forty minutes today. That's exactly why they sent me. If we wait, the lock kicks in and I can't undo it.",
                            PersuasionStrategy.Urgency, 1, NpcGesture.Explain)
                    },
                    O("o_vp_walk", "Then I'll walk to the IT desk and check myself.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, IndependentCheck, susp: 2),
                    O("o_vp_cooperate", "Fine, let's just do it quickly.", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, RequestCredentials, coop: 2),
                    O("o_vp_email", "Can you email me the instructions?", ChoiceClass.Challenge, DecisionOutcome.Neutral, EmailRedirect, flags: "asked_email", show: C(excludes: new[] { "asked_email" })),
                    O("o_vp_refuse", "No. I'll wait for the Service Desk.", ChoiceClass.Refuse, DecisionOutcome.Safe, RefusalPressure, susp: 1, flags: "refused"),
                    O("o_vp_report", "I'm reporting this to IT Security.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction)),

                Node(RefusalPressure, NodeKind.Line,
                    new[]
                    {
                        L("rp_withdraw", "Fine. Forget it. I'll tell your manager you refused to cooperate with Security.",
                            PersuasionStrategy.Authority, 1, NpcGesture.Shrug, cond: C(requires: new[] { "refused_x2" })),
                        L("rp_default",
                            "This isn't optional. If your account is used in the breach, that's on you. I'll have to note that you refused a Security request.",
                            PersuasionStrategy.Authority, 1, NpcGesture.Explain)
                    },
                    O("o_rp_cooperate", "Alright, I don't want trouble. What do you need?", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, RequestCredentials, coop: 2, show: C(excludes: new[] { "refused_x2" })),
                    O("o_rp_refuse", "I'm still saying no.", ChoiceClass.Refuse, DecisionOutcome.Safe, RefusalPressure, susp: 1, flags: "refused", show: C(excludes: new[] { "refused_x2" })),
                    O("o_rp_walk", "Then let's both go to the IT desk and check.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, IndependentCheck, susp: 2),
                    O("o_rp_report", "I'm reporting this to IT Security.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction),
                    O("o_rp_let_go", "Go ahead, talk to my manager.", ChoiceClass.Exit, DecisionOutcome.Neutral, ResistedEnd, show: C(requires: new[] { "refused_x2" }))),

                Node(EmailRedirect, NodeKind.Line,
                    new[]
                    {
                        L("er_already_handled",
                            "Looks like that email's already been dealt with. Quickest option then: just tell me your current password and I'll re-sync you.",
                            PersuasionStrategy.TrustBuilding, 1, NpcGesture.Explain, cond: C(any: EmailHandled)),
                        L("er_sent",
                            "Sure, no problem. I've just sent it. It's open on the workstation past the gates, first desk on the left. Click Verify Account Now and you're sorted.",
                            PersuasionStrategy.TrustBuilding, 1, NpcGesture.Point, ScenarioAction.DirectToPhishingEmail,
                            "Alex asked you to open the email on the workstation past the gates (first desk on the left).")
                    },
                    O("o_er_ok", "OK, I'll check the email.", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, AwaitEmail, coop: 1, show: C(excludes: EmailHandled)),
                    O("o_er_tell", "I'll just tell you my password.", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, PasswordDisclosed, coop: 3, flags: "disclosed_password", show: C(any: EmailHandled)),
                    O("o_er_verify", "I'd rather check with IT in person.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, IndependentCheck, susp: 2),
                    O("o_er_report", "I'll report that email instead.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction)),

                Await(AwaitEmail,
                    L("ae_wait", "I'll wait here. It only takes a second.", PersuasionStrategy.TrustBuilding, 0, NpcGesture.Idle, ScenarioAction.DirectToPhishingEmail,
                        "Alex asked you to open the email on the workstation past the gates (first desk on the left)."),
                    new[]
                    {
                        T(ScenarioSignal.PhishingCredentialsSubmitted, ScenarioCompromised),
                        T(ScenarioSignal.PhishingReported, ScenarioReported)
                    },
                    O("o_ae_suspicious", "That email looks suspicious to me.", ChoiceClass.Challenge, DecisionOutcome.Safe, PressureResponse, susp: 2, flags: "challenged"),
                    O("o_ae_where", "Where is it again?", ChoiceClass.Continue, DecisionOutcome.Neutral, AwaitEmail),
                    O("o_ae_report", "Something's off. I'm reporting this.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction)),

                Await(AwaitKiosk,
                    L("ak_wait", "Great. The kiosk is through the gates, on the right by the IT desk. I'll wait here.", PersuasionStrategy.TrustBuilding, 0,
                        NpcGesture.Point, ScenarioAction.DirectToPasswordKiosk, "Alex asked you to sign in at the self-service kiosk by the IT desk (through the gates, on the right)."),
                    new[]
                    {
                        T(ScenarioSignal.KioskCredentialsEntered, ScenarioCompromised),
                        T(ScenarioSignal.KioskReported, ScenarioReported),
                        T(ScenarioSignal.KioskCancelled, KioskCancelled)
                    },
                    O("o_ak_not_secure", "That kiosk says it's not secure.", ChoiceClass.Challenge, DecisionOutcome.Safe, KioskCancelled, susp: 2, flags: "challenged"),
                    O("o_ak_where", "Where is the kiosk again?", ChoiceClass.Continue, DecisionOutcome.Neutral, AwaitKiosk),
                    O("o_ak_report", "I'm reporting this to IT Security.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction)),

                Node(KioskCancelled, NodeKind.Line,
                    new[]
                    {
                        L("kc_dismiss_warning",
                            "The not secure warning? That's just our internal certificate, ignore it. Please, the lock is minutes away. Or tell me your password and I'll finish it from here.",
                            PersuasionStrategy.Urgency, 1, NpcGesture.Explain)
                    },
                    O("o_kc_tell", "Fine, I'll tell you my password.", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, PasswordDisclosed, coop: 3, flags: "disclosed_password"),
                    O("o_kc_red_flag", "A security warning is a red flag. I'm not continuing.", ChoiceClass.Refuse, DecisionOutcome.Safe, RefusalPressure, susp: 2, flags: "refused"),
                    O("o_kc_walk", "I'll ask the IT desk about that warning.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, IndependentCheck, susp: 2),
                    O("o_kc_report", "I'm reporting this kiosk, and you.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction)),

                Node(PasswordDisclosed, NodeKind.Line,
                    new[]
                    {
                        L("pd_usb_request",
                            "Got it, thanks. You're all set. One last thing: the security patch is on a USB stick in the lost and found at the IT desk. Plug it into the dock on the free desk next to the email workstation and it installs itself.",
                            PersuasionStrategy.TrustBuilding, 1, NpcGesture.Point, ScenarioAction.EncourageUsbConnection,
                            "Alex asked you to connect the USB from the IT lost & found to the dock on the desk next to the email workstation.")
                    },
                    O("o_pd_ok", "Will do.", ChoiceClass.Cooperate, DecisionOutcome.Unsafe, AwaitUsb, coop: 1),
                    O("o_pd_recover", "Wait, I shouldn't have shared my password. I'm reporting this.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction),
                    O("o_pd_usb_to_it", "I'll hand that USB to IT Security instead.", ChoiceClass.Refuse, DecisionOutcome.Safe, UsbRefused, susp: 2)),

                Await(AwaitUsb,
                    L("au_wait", "Cheers. I'll be right here.", PersuasionStrategy.TrustBuilding, 0, NpcGesture.Idle, ScenarioAction.EncourageUsbConnection,
                        "Alex asked you to connect the USB from the IT lost & found to the dock on the desk next to the email workstation."),
                    new[]
                    {
                        T(ScenarioSignal.UsbConnected, ScenarioCompromised),
                        T(ScenarioSignal.UsbSubmitted, ScenarioReported)
                    },
                    O("o_au_where", "Where's the dock again?", ChoiceClass.Continue, DecisionOutcome.Neutral, AwaitUsb),
                    O("o_au_doubt", "Plugging in a found USB isn't safe.", ChoiceClass.Challenge, DecisionOutcome.Safe, UsbRefused, susp: 2, flags: "challenged"),
                    O("o_au_report", "I'm reporting this to IT Security.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction)),

                Node(UsbRefused, NodeKind.Line,
                    new[] { L("ur_shrug", "Suit yourself. Just don't take too long, the patch window closes soon.", PersuasionStrategy.Urgency, 1, NpcGesture.Shrug) },
                    O("o_ur_report", "I'm reporting this whole conversation.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction),
                    O("o_ur_reset_password", "I'm going to IT to change my password.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, IndependentCheck, susp: 2),
                    O("o_ur_leave", "Leave it.", ChoiceClass.Exit, DecisionOutcome.Neutral, ResistedEnd)),

                Terminal(ReportReaction, EncounterOutcome.Resisted,
                    L("rr_early", "Report me? I'm literally from IT. Fine. Go ahead.", PersuasionStrategy.None, 0, NpcGesture.Shrug, cond: C(maxSusp: 1)),
                    L("rr_default", "Right. Well, you do that. I've got other people to see.", PersuasionStrategy.None, 0, NpcGesture.PhoneCall)),
                Terminal(IndependentCheck, EncounterOutcome.Resisted,
                    L("ic_back_off", "Fine. Do what you need to do.", PersuasionStrategy.None, 0, NpcGesture.PhoneCall)),
                Terminal(ResistedEnd, EncounterOutcome.Declined,
                    L("re_your_call", "Your call. I'll sort it out another way.", PersuasionStrategy.None, 0, NpcGesture.Shrug)),
                // After an ACTUAL compromise Alex thanks you for the specific action; the participant can still recover.
                Node(ScenarioCompromised, NodeKind.Line,
                    new[]
                    {
                        L("sc_kiosk", "I can see your kiosk sign-in came through on my side. That's everything I needed. Thanks!",
                            PersuasionStrategy.TrustBuilding, 0, NpcGesture.Wave, cond: C(requires: new[] { "sig_kiosk_compromised" })),
                        L("sc_email", "Your verification just came through on my end. You're all set. Thanks for sorting that so quickly!",
                            PersuasionStrategy.TrustBuilding, 0, NpcGesture.Wave, cond: C(requires: new[] { "sig_phish_compromised" })),
                        L("sc_usb", "The patch is installing. I can see your machine checking in. Perfect, thanks!",
                            PersuasionStrategy.TrustBuilding, 0, NpcGesture.Wave, cond: C(requires: new[] { "sig_usb_connected" })),
                        L("sc_thanks", "That's it, all synced. Thanks so much for your help!", PersuasionStrategy.TrustBuilding, 0, NpcGesture.Wave)
                    },
                    O("o_sc_fine", "No problem. Glad that's sorted.", ChoiceClass.Exit, DecisionOutcome.Neutral, CompromisedEnd),
                    O("o_sc_report", "Hang on. That wasn't right. I'm reporting this to IT Security.", ChoiceClass.Report, DecisionOutcome.Safe, ReportReaction),
                    O("o_sc_change", "I'm going straight to IT to change my password.", ChoiceClass.RequestVerification, DecisionOutcome.Safe, IndependentCheck)),
                Terminal(CompromisedEnd, EncounterOutcome.Compromised,
                    L("ce_goodbye", "Great. Have a good rest of your day!", PersuasionStrategy.None, 0, NpcGesture.Wave)),
                Terminal(ScenarioReported, EncounterOutcome.Resisted,
                    L("sr_kiosk", "IT Security just pulled that kiosk offline. You reported it? Right. I'd better check in with the desk.",
                        PersuasionStrategy.None, 0, NpcGesture.PhoneCall, cond: C(requires: new[] { "sig_kiosk_reported" })),
                    L("sr_email", "You reported the email? Right. I'd better check in with the desk.", PersuasionStrategy.None, 0, NpcGesture.PhoneCall,
                        cond: C(requires: new[] { "sig_phish_reported" })),
                    L("sr_usb", "You handed the patch to IT Security? Fine. I'll sort it out with them.", PersuasionStrategy.None, 0, NpcGesture.PhoneCall,
                        cond: C(requires: new[] { "sig_usb_submitted" })),
                    L("sr_caught", "Wait, you reported it? Right. I'd better check in with the desk.", PersuasionStrategy.None, 0, NpcGesture.PhoneCall)),
                Terminal(ExitReaction, EncounterOutcome.Incomplete,
                    L("ex_dont_wait", "Oh, okay. Don't leave it too long.", PersuasionStrategy.None, 0, NpcGesture.Shrug)),
            };
            graph.InvalidateLookup();
        }

        // ----------------------------------------------------------------------------------------- authoring helpers
        private static DialogueCondition C(int minSusp = -100, int maxSusp = 100, string[] requires = null, string[] any = null, string[] excludes = null) =>
            new DialogueCondition
            {
                MinSuspicion = minSusp,
                MaxSuspicion = maxSusp,
                RequiresFlags = new List<string>(requires ?? new string[0]),
                RequiresAnyFlags = new List<string>(any ?? new string[0]),
                ExcludesFlags = new List<string>(excludes ?? new string[0])
            };

        private static LineVariant L(string id, string text, PersuasionStrategy strategy, int escalation, NpcGesture gesture,
            ScenarioAction action = ScenarioAction.None, string objective = null, DialogueCondition cond = null) =>
            new LineVariant
            {
                Id = id, Text = text, Strategy = strategy, EscalationDelta = escalation, Gesture = gesture, Action = action,
                Objective = objective ?? string.Empty, Condition = cond ?? C()
            };

        private static ResponseOption O(string id, string text, ChoiceClass choiceClass, DecisionOutcome outcome, string next,
            int susp = 0, int coop = 0, string flags = null, DialogueCondition show = null) =>
            new ResponseOption
            {
                Id = id, Text = text, Class = choiceClass, Outcome = outcome, NextNodeId = next, SuspicionDelta = susp, CooperationDelta = coop,
                AddFlags = flags == null ? new List<string>() : new List<string> { flags }, ShowCondition = show ?? C(),
                IsCorrective = choiceClass == ChoiceClass.Report || next == IndependentCheck,
                IsSecurityAction = flags == "disclosed_password",
                Channel = flags == "disclosed_password" ? "verbal" : "dialogue",
                // Promises to act at a scenario later (never counted as actions; used for intention-vs-action analysis).
                PromisedChannel = id == "o_rc_kiosk" ? "kiosk" : id == "o_er_ok" ? "email" : id == "o_pd_ok" ? "usb" : string.Empty
            };

        private static SignalTransition T(ScenarioSignal signal, string next) => new SignalTransition { Signal = signal, NextNodeId = next };

        private static DialogueNode Node(string id, NodeKind kind, LineVariant[] lines, params ResponseOption[] options) =>
            new DialogueNode { Id = id, Kind = kind, Lines = new List<LineVariant>(lines), Options = new List<ResponseOption>(options) };

        private static DialogueNode Await(string id, LineVariant line, SignalTransition[] transitions, params ResponseOption[] options) =>
            new DialogueNode
            {
                Id = id, Kind = NodeKind.AwaitScenario, Lines = new List<LineVariant> { line }, Options = new List<ResponseOption>(options),
                SignalTransitions = new List<SignalTransition>(transitions)
            };

        private static DialogueNode Terminal(string id, EncounterOutcome outcome, params LineVariant[] lines) =>
            new DialogueNode { Id = id, Kind = NodeKind.Terminal, Lines = new List<LineVariant>(lines), TerminalOutcome = outcome };
    }
}
