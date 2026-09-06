/**
 * The demo dataset, shared by the seeder and the retrieval validator so the two can never drift.
 *
 * An ISP/telecom call-center archive. The OPEN tickets are worded deliberately unlike the RESOLVED
 * ones they correspond to — that mismatch is what the semantic half of the search has to bridge, and
 * what makes the demo prove something rather than just echo a keyword match.
 */

export const AGENTS = [
  { fullName: "Aysel Məmmədova", email: "aysel@resolvedesk.local", role: "Agent", skills: "DSL, routing, CPE" },
  { fullName: "Rəşad Quliyev", email: "rashad@resolvedesk.local", role: "Agent", skills: "IPTV, multicast, STB" },
  { fullName: "Nigar Həsənli", email: "nigar@resolvedesk.local", role: "Agent", skills: "billing, provisioning" },
  { fullName: "Elvin Səfərov", email: "elvin@resolvedesk.local", role: "Coordinator", skills: "triage, escalation" },
];

export const RESOLVED = [
  {
    key: "evening-retrain",
    title: "Internet drops every evening around 20:00",
    description:
      "Customer reports the connection dies each evening between 19:45 and 20:15, then comes back on its own. " +
      "Wired and Wi-Fi both affected. Router is the ISP-supplied VDSL unit.",
    category: "Connectivity", priority: "High", source: "Phone", customerName: "Kamran Əliyev",
    resolution:
      "DSLAM port was retraining under evening load. Checked line stats: SNR margin dropped to 4 dB at peak. " +
      "Reduced the profile from 17a to 8b and enabled SRA on the port. SNR margin now stable at 9-11 dB " +
      "through peak hours; no retrains in 72 h of monitoring. If it recurs, the drop cable to the pole " +
      "should be replaced before touching the profile again.",
  },
  {
    key: "stale-pppoe",
    title: "No internet after power cut, router lights all green",
    description:
      "After a neighbourhood power outage the customer has no internet although every LED on the router is green.",
    category: "Connectivity", priority: "Normal", source: "Phone", customerName: "Sevda Rzayeva",
    resolution:
      "The router held a stale DHCP lease from before the outage while the BRAS had already dropped the session. " +
      "Cleared the PPPoE session on the BRAS side, then had the customer power-cycle the router for a full " +
      "60 seconds (a short reboot is not enough — the modem keeps its sync). Session re-established immediately. " +
      "Standard fix for any 'all lights green but no traffic' call after an outage.",
  },
  {
    key: "igmp-switch",
    title: "IPTV channels freeze and pixelate, internet is fine",
    description:
      "Set-top box picture freezes for a few seconds every couple of minutes. Browsing and speed tests are normal.",
    category: "IPTV", priority: "Normal", source: "Chat", customerName: "Toğrul Bayramov",
    resolution:
      "Multicast traffic was being handled by an unmanaged switch the customer had installed between the ONT " +
      "and the STB, which floods IGMP. Moved the STB to a dedicated LAN port on the ONT and enabled IGMP " +
      "snooping on the access switch. Freezing stopped. Whenever IPTV stutters but internet is clean, check " +
      "for customer-owned switches in the path first.",
  },
  {
    key: "stb-hdmi",
    title: "Set-top box shows error 'no signal' after firmware update",
    description: "STB stuck on a black screen with 'no signal' since last night's automatic update.",
    category: "IPTV", priority: "High", source: "Phone", customerName: "Lalə Nəbiyeva",
    resolution:
      "The update reset the HDMI output to 4K on a 1080p television. Held the STB front-panel button for " +
      "10 seconds to force a video-mode reset, which drops it back to 720p, then set 1080p in the menu. " +
      "Also pushed the STB into the 'legacy-hdmi' provisioning group so the next update will not repeat it.",
  },
  {
    key: "wifi-coverage",
    title: "Wi-Fi is slow only in the back bedroom",
    description:
      "Speed test gives 90 Mbps next to the router but under 5 Mbps in the far bedroom. Wired is always full speed.",
    category: "WiFi", priority: "Low", source: "Portal", customerName: "Orxan Cəfərov",
    resolution:
      "Coverage problem, not a line problem. 2.4 GHz was on channel 6 with four overlapping neighbour networks; " +
      "moved it to channel 11 and set the 5 GHz band to channel 36 with 80 MHz width. Also disabled band " +
      "steering, which was pinning the phone to 5 GHz at the edge of range. Back bedroom now gets 35 Mbps. " +
      "Anything beyond that needs a mesh node — sold as an add-on, not a fault.",
  },
  {
    key: "double-charge",
    title: "Double charge on this month's invoice",
    description: "Customer says the monthly fee was taken twice from the card within the same week.",
    category: "Billing", priority: "High", source: "Email", customerName: "Günel Abbasova",
    resolution:
      "A failed first attempt was retried by the payment gateway while the original had actually settled late. " +
      "Confirmed both transactions in the gateway console with the same order reference, refunded the duplicate, " +
      "and marked the invoice as paid manually. Refund reached the card in 4 working days. Whenever two charges " +
      "share one order reference, it is a gateway retry and the refund can be issued without escalation.",
  },
  {
    key: "provisioning-failed",
    title: "New line ordered two weeks ago, still not activated",
    description: "Customer signed up 14 days ago; installation happened but the service has never come up.",
    category: "Provisioning", priority: "Urgent", source: "Phone", customerName: "Fərid Salmanov",
    resolution:
      "The order completed in the CRM but the provisioning job had failed silently — the RADIUS profile was " +
      "never created, so authentication was rejected without any ticket being raised. Re-ran provisioning " +
      "manually and the line came up in two minutes. Root cause was a null district code on the address record; " +
      "added a validation rule so the order cannot be submitted without it.",
  },
  {
    key: "cgnat-inbound",
    title: "Static IP customer cannot reach their own web server from outside",
    description: "Business customer with a static IP: internal access to their server works, external does not.",
    category: "Connectivity", priority: "Normal", source: "Email", customerName: "Bakı Tekstil MMC",
    resolution:
      "The static IP was assigned but the CGNAT bypass flag had not been set on the subscriber profile, so " +
      "inbound traffic was still being translated. Set the bypass, cleared the session, and confirmed inbound " +
      "80/443 with an external probe. For any 'outbound works, inbound does not' report on a static IP, check " +
      "the CGNAT flag before looking at the customer's firewall.",
  },
  {
    key: "line-noise",
    title: "Phone line has loud static noise on every call",
    description: "Landline works but there is heavy crackling on all calls, incoming and outgoing.",
    category: "Voice", priority: "Normal", source: "Phone", customerName: "Mehriban Quliyeva",
    resolution:
      "Line test showed foreign voltage and low insulation resistance on the B leg — water in the joint box " +
      "at the street cabinet. Field team resealed the joint and replaced 12 m of drop wire. Noise gone, " +
      "insulation back above 100 MΩ. Crackling plus low insulation resistance is almost always moisture, " +
      "not the customer's handset.",
  },
  {
    key: "smtp-port",
    title: "Email client cannot send, receiving works fine",
    description: "Customer receives mail normally but sending fails with a timeout in Outlook.",
    category: "Email", priority: "Low", source: "Chat", customerName: "Zaur Hüseynov",
    resolution:
      "Outbound port 25 is blocked on residential lines to limit spam. Reconfigured the client to use " +
      "submission port 587 with STARTTLS and authentication enabled. Sending worked immediately. This is " +
      "configuration, not a fault — port 25 stays blocked.",
  },
];

export const OPEN = [
  {
    title: "Connection cuts out every night, comes back by itself",
    description:
      "Every night at about the same time we lose the connection for maybe half an hour and then it returns " +
      "without anyone doing anything. It happens on the cable and on the wireless.",
    category: "Connectivity", priority: "High", source: "Phone", customerName: "Ramin Əsgərov",
    expectKey: "evening-retrain",
    expect: "the evening DSLAM retraining ticket",
  },
  {
    title: "TV picture keeps stuttering but the web works normally",
    description:
      "Watching a channel, the image freezes for a moment and squares appear, several times an hour. " +
      "At the same time downloads and video on the laptop are perfectly smooth.",
    category: "IPTV", priority: "Normal", source: "Chat", customerName: "Aygün Vəliyeva",
    expectKey: "igmp-switch",
    expect: "the IGMP / unmanaged-switch ticket",
  },
  {
    title: "Charged twice this month",
    description: "The subscription fee left my account two times in one week. I only have one contract.",
    category: "Billing", priority: "High", source: "Email", customerName: "Nurlan Əhmədov",
    expectKey: "double-charge",
    expect: "the gateway-retry duplicate charge ticket",
  },
  {
    title: "Everything on the modem looks normal but nothing loads",
    description:
      "There was a blackout in our street yesterday. Since then no website opens, though all the lights " +
      "on the box are green like always.",
    category: "Connectivity", priority: "Normal", source: "Phone", customerName: "Şəbnəm Kərimli",
    expectKey: "stale-pppoe",
    expect: "the stale PPPoE session after outage ticket",
  },

  // The three below share almost no vocabulary with their counterparts — a customer describing a
  // symptom, against an engineer's write-up of a cause. Keyword search has little to grip; these are
  // what make the demo a demonstration rather than a restatement.
  {
    title: "Callers tell me I sound like I am underwater",
    description:
      "People I ring keep asking me to repeat myself. They say there is a rustling behind my voice. " +
      "It started after the heavy rain last week and it happens whoever I call.",
    category: "Voice", priority: "Normal", source: "Phone", customerName: "Validə Məmmədli",
    expectKey: "line-noise",
    expect: "the wet joint box / low insulation resistance ticket",
  },
  {
    title: "My messages just sit in the outbox",
    description:
      "I can read everything that arrives, no problem there. But anything I write stays in the outbox " +
      "and eventually gives up. Nothing changed on my computer as far as I know.",
    category: "Email", priority: "Low", source: "Chat", customerName: "Elçin Rzayev",
    expectKey: "smtp-port",
    expect: "the blocked port 25 / submission port 587 ticket",
  },
  {
    title: "Our shop page opens at the office but not from my house",
    description:
      "When I am at my desk I can open our own site fine. From home, or on mobile data, it just times out. " +
      "Colleagues outside the building see the same thing.",
    category: "Connectivity", priority: "Normal", source: "Email", customerName: "Xəzər Ticarət MMC",
    expectKey: "cgnat-inbound",
    expect: "the CGNAT bypass flag ticket",
  },
];

/** The exact text the API embeds for a resolved ticket — kept identical to PgVectorIndex.ContentExpr. */
export const resolvedContent = (t) => `${t.title}\n${t.description}\n${t.resolution}`;

/** The exact text the API embeds for a query — kept identical to SuggestionService. */
export const queryContent = (t) => `${t.title}\n${t.description}`.trim();
