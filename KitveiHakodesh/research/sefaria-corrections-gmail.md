# Sending the Sefaria Correction Email via Gmail

Companion to [`sefaria-corrections.md`](./sefaria-corrections.md), which covers **what goes in the
report** (Sefaria's four required fields: page URL, text version, current text, proposed text +
basis, sent to `corrections@sefaria.org`). That part is settled — don't re-derive it here.

This document covers only the **sending mechanism**: can KitveiHakodesh put a "Report a typo"
button in the reader that emails Sefaria automatically, for free, using Gmail?

Everything below was checked against live sources in September 2026. Where something is Google
policy that could change, or where I'm reasoning rather than citing, it's marked.

---

## Summary / recommendation

**For a personal build (you, on your own machine): yes.** Gmail SMTP with an App Password from
your own account, sent from the C# host via MailKit, works today and is free. Setup is ~5 minutes.
Free-Gmail cap is 500 messages/day — irrelevant for this use case.

**For a build shipped to other users: no, not with one Gmail account.** Whichever mechanism you
pick, an App Password or OAuth client secret compiled into the binary is extractable by anyone who
has the binary, and every user's corrections would go out as *you*, from *your* mailbox, tripping
Gmail's abuse heuristics. That's not a hardening problem you can solve with obfuscation — it's
structural.

So the honest split is:

| Build | Recommended mechanism |
|---|---|
| Personal / internal | Gmail SMTP + your own App Password, MailKit, credential in Windows DPAPI-protected local config (not source) |
| Shipped to users | **`mailto:`** — pre-fill To/Subject/Body, let the user's own mail client send it. One click, zero credentials, zero quota, zero liability. |
| Shipped, and silent send is a hard requirement | A relay you operate (EmailJS/Resend/your own endpoint) with a per-install rate limit — accepting that you now run mail infrastructure and pay for abuse |

The `mailto:` conclusion is the same one §6 of the companion doc reached. Nothing found in this
research overturns it; this document is the detailed *why*.

---

## 1. Gmail SMTP with an App Password — still supported in 2026

**Documented fact.** App Passwords still work. Google's current page is
[Sign in with app passwords](https://support.google.com/accounts/answer/185833).

Requirements as of now:

- **2-Step Verification must be enabled** on the account. Google will not show the App Password
  generator otherwise. There is no way around this.
- Generate at <https://myaccount.google.com/apppasswords> — you get a **16-character** password,
  shown once. It is *not* your account password.
- Server settings:

  | | |
  |---|---|
  | Host | `smtp.gmail.com` |
  | Port 587 | STARTTLS — the recommended modern option |
  | Port 465 | Implicit TLS/SSL — still works |
  | Username | the full Gmail address |
  | Password | the 16-char App Password |

**What already changed:** "Less secure app access" (plain username + real password) was shut down
progressively from **30 September 2024** and finished on **1 May 2025**. Since then every SMTP
connection to Gmail must use either an App Password or an OAuth 2.0 token.

**Known exception:** accounts enrolled in Google's
[Advanced Protection Program](https://support.google.com/accounts/answer/7539956) cannot use App
Passwords at all — enrolling **revokes existing ones**. If a user of a per-user-configured build
is on Advanced Protection, SMTP simply won't be available to them.

**Google Workspace accounts — resolved (was flagged uncertain, now verified).** App Passwords were
**explicitly carved out** of the LSA shutdown. Google's own transition guidance states:

> "You will no longer use a password for access (**with the exception of app passwords**)."

and, for SMTP devices that can't move to OAuth: *"Configure an app password for use with the
device."* So OAuth2 is **not** mandatory on Workspace — App Passwords remain the sanctioned SMTP
fallback there too.

The admin controls, precisely:
- The old *"less secure apps"* enforcement setting **has been removed from the Admin console** —
  that lever is gone.
- What admins retain is **visibility and revocation** of users' app passwords.
- The effective suppression lever is the 2SV method policy (Admin console → Security →
  Authentication → 2-step verification): setting **"Only security key"** makes App Passwords
  unavailable, since they require a non-security-key 2SV method.
- **Genuinely ambiguous:** no first-party Google doc names a dedicated *"allow app passwords"*
  toggle. Third-party posts that name one aren't corroborated. Flagging rather than asserting.

Relevant dates, all first-party: **14 March 2025** — all LSA access terminates (IMAP/SMTP/POP stop
working with basic auth); **1 May 2025** — Workspace accounts no longer support sign-in with plain
username and password.

**Deprecation risk (inference, not a Google announcement):** no announced end-of-life for App
Passwords. But Google has removed one legacy auth path already (LSA, 2024–25) and is pushing
passkeys hard. Google's own page says *"App passwords aren't recommended and are unnecessary in most
cases."* Treat them as *currently supported but strategically disfavoured* — fine for a personal
tool, a poor thing to build a shipped product's only path on.

---

## 2. Gmail API with OAuth2 (`gmail.send`) — the real blocker for a shipped app

This is the question that decides the whole design.

**The flow for a desktop app.** You create a Google Cloud project, enable the Gmail API, configure
an OAuth consent screen, and create an OAuth client of type *Desktop app*. At runtime the app opens
the system browser, the user signs in to *their own* Google account and consents, and the app
receives an authorization code on a `localhost` redirect, which it exchanges for access + refresh
tokens. This is the standard installed-app flow (PKCE). **Note:** a "Desktop app" OAuth client is
public — its client secret is *not* a secret and Google says so; security comes from the redirect
being on the user's own machine. That part is fine.

**The blocker is scope classification.** `https://www.googleapis.com/auth/gmail.send` is classified
by Google as a **sensitive** scope (not "restricted" — that's the read/modify mailbox scopes, which
additionally require an annual third-party CASA security assessment). Sensitive is the lighter
tier, but it still requires verification for public distribution:
[Sensitive scope verification](https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification).

While your OAuth consent screen publishing status is **Testing**, you avoid verification, but you
inherit two hard caps:

- **Maximum 100 test users**, each individually added by email address in the Cloud Console
  ([Manage App Audience](https://support.google.com/cloud/answer/15549945)).
- **Refresh tokens expire after 7 days.** Google's own docs:
  [OAuth 2.0 overview](https://developers.google.com/identity/protocols/oauth2) — a project with an
  external consent screen in Testing status "is issued a refresh token expiring in 7 days" unless
  the only scopes are name/email/profile. `gmail.send` is not in that exempt subset.

Read that second point carefully: **in Testing mode, every user has to re-consent weekly.** That
alone kills Testing mode as a shipping strategy — a typo-report button that demands a browser
sign-in every seven days will not be used.

Users also see the **"Google hasn't verified this app"** interstitial and must click through
Advanced → Go to (unsafe) — for a Hebrew-speaking non-technical seforim audience, that is a
non-starter on its own.

**To publish (removes both caps), verification requires:**

1. A **privacy policy** on a domain you own, disclosing how you access/use/store/share Google user
   data.
2. **Domain ownership verified** through Google Search Console.
3. A publicly accessible **app homepage** on that domain.
4. An **unlisted YouTube demo video** showing the real OAuth consent flow and where the scope is
   used in your app.
5. **Written justification** for the scope, including why a narrower scope won't do.

Google states sensitive-scope review typically takes **3–5 business days**. There is no listed fee
(unlike restricted scopes, where the CASA assessment carries a real annual cost). In practice
reviews commonly bounce once or twice over branding mismatches between the consent screen and the
live homepage.

**Verdict on the Gmail API:** it is the *correct* answer to "each user sends as themselves", and it
is genuinely free of per-message cost — but it obliges you to own a domain, publish a privacy
policy, maintain a Google Cloud project, and pass and re-pass a review. That's real ongoing product
overhead for one button. Only worth it if this app is going to be a properly published product.

---

## 3. Sending limits and quotas

**Documented fact.**

| Account type | Limit | Source |
|---|---|---|
| Free consumer `@gmail.com` | **500 messages/day** and **500 recipients per message**; recovery in **1–24 hours** | [Gmail sending limits](https://support.google.com/mail/answer/22839) |
| Google Workspace (paid) | **2,000 messages/day**; **10,000 total recipients/day**; **3,000 external recipients/day** | [Gmail sending limits in Workspace](https://knowledge.workspace.google.com/admin/gmail/gmail-sending-limits-in-google-workspace) |
| Workspace **trial** | 500/day | same |

Two limits specific to how you send:

- Via **SMTP/POP/IMAP**: **100 recipients per message** (much lower than the web UI's 2,000).
- Via the **Gmail API**: 500 recipients per message.

Neither matters here — a Sefaria correction email has exactly one recipient, and the companion doc
already says to send **one email per correction**, not batches.

**On exceeding:** you get an error and cannot send for **up to 24 hours**. The account keeps
working otherwise (receiving mail, other Google services). Limits are a **rolling 24-hour window**,
not a midnight reset. Gmail also throttles you the same way if a high proportion of your messages
bounce.

**What this means in practice.** 500/day is enormous for personal use. But for a shipped app
funnelling every user's report through *one* mailbox, 500/day is the ceiling for the entire user
base combined — and see §7 for why you'd hit trouble long before that.

---

## 4. From the Vue/browser side: SMTP is impossible, full stop

**Documented fact, not opinion.** A browser page (and therefore the WebView2-hosted Vue frontend)
cannot speak SMTP. JavaScript in a page has no raw TCP socket API — only HTTP(S), WebSocket, and
WebRTC, all of which require a cooperating server speaking that protocol. `smtp.gmail.com` speaks
SMTP over TCP; there is no gateway. Any npm package named `nodemailer`, `smtp-client`, etc. is a
**Node.js** package and will not run in a browser context.

Microsoft's own annotation on `SmtpClient` corroborates the general point — the class carries
`[UnsupportedOSPlatform("browser")]`, meaning even .NET marks SMTP as unavailable when the runtime
target is a browser.

**Consequence for this app's split:** the Vue side can only ever do one of three things:

1. Open a `mailto:` URL (the OS hands it to the user's mail client), or
2. Call a **third-party HTTPS email API** (EmailJS, Resend, Web3Forms…) with a key embedded in the
   frontend bundle — where the key is trivially readable via DevTools, and
3. Call into the **C# host** over the WebView2 bridge and let the native side do the actual sending.

Option 3 is the one that's genuinely available to KitveiHakodesh and not to a plain web app. The
C# host is a real Win32 process with unrestricted sockets and filesystem — it can do SMTP properly
and can store a credential outside the JS bundle. That's the architectural advantage worth using
if you go the SMTP route at all.

---

## 5. .NET: don't use `System.Net.Mail.SmtpClient` — use MailKit

**Documented fact, quoted from Microsoft.** The
[`SmtpClient` class reference](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient)
carries this in its Remarks, verbatim:

> We don't recommend that you use the `SmtpClient` class for new development because `SmtpClient`
> doesn't support many modern protocols. Use MailKit or other libraries instead.

The class summary itself reads: "The SmtpClient type is not recommended for new development." The
linked rationale is Microsoft's platform-compat notice
[DE0005: SmtpClient shouldn't be used](https://github.com/dotnet/platform-compat/blob/master/docs/DE0005.md).

Note it is a *recommendation*, not a compile-time `[Obsolete]` error — `SmtpClient` still works and
still ships in .NET 10/11. But there's no reason to choose it for new code.

**MailKit** (<https://github.com/jstedfast/MailKit>, NuGet `MailKit`, MIT licence, free) is the
recommended replacement. It supports STARTTLS and OAuth2 bearer auth properly, which `SmtpClient`
does not.

### Illustrative snippet — do not add this to the project

```csharp
// Illustration only. Credential comes from local per-user config, never from source.
var msg = new MimeMessage();
msg.From.Add(new MailboxAddress("KitveiHakodesh", senderAddress));
msg.To.Add(new MailboxAddress("Sefaria Corrections", "corrections@sefaria.org"));
msg.Subject = "Text correction — בראשית א:א";   // UTF-8, see §5.1

msg.Body = new TextPart("plain")
{
    Text = reportBody                 // the 4 fields from sefaria-corrections.md §5
};

using var smtp = new MailKit.Net.Smtp.SmtpClient();
await smtp.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
await smtp.AuthenticateAsync(gmailAddress, appPassword);   // 16-char App Password
await smtp.SendAsync(msg);
await smtp.DisconnectAsync(true);
```

### 5.1 Hebrew / UTF-8 notes

This matters more than usual here — the whole payload is Hebrew, often with nikud and te'amim.

- **Body and subject.** MailKit's `TextPart("plain")` defaults to UTF-8 and will emit
  `Content-Type: text/plain; charset=utf-8` with the appropriate transfer encoding automatically.
  You generally don't need to set anything. With the legacy `System.Net.Mail` you'd have to set
  `BodyEncoding` and `SubjectEncoding` to `Encoding.UTF8` by hand — another small reason to prefer
  MailKit.
- **Subject line.** Non-ASCII subjects are RFC 2047 encoded-words (`=?utf-8?B?...?=`). MailKit does
  this for you; a hand-rolled SMTP conversation would not, and Gmail would reject or mangle it.
- **Send plain text, not HTML.** The companion doc's four-field template is plain text and is meant
  to be read by a human at Sefaria. Plain text also sidesteps a whole class of HTML-entity and
  bidi-markup damage to the very characters you're reporting.
- **Nikud and te'amim are combining marks.** They must survive as-is. Don't normalize (no
  `String.Normalize()`), don't strip, don't "clean" the string anywhere on the C#↔Vue bridge. NFC
  vs NFD normalization can silently alter exactly the sequence you are asking Sefaria to fix.
- **Bidi in the body.** Because Hebrew is RTL and the surrounding template labels are LTR, the
  reader's rendering of a mixed line can look scrambled even when the bytes are perfect. Put each
  Hebrew string **on its own line** (as the companion doc's template already does) rather than
  inline after an English label.
- **`mailto:` specifically.** The body must be **percent-encoded UTF-8**
  (`encodeURIComponent`), not raw Hebrew in the URL. Practical caveat: `mailto:` URLs have real
  length limits — historically ~2000 characters on Windows, and percent-encoding Hebrew costs
  **~9 bytes per character** (3 bytes UTF-8 × 3 chars each as `%XX`). A four-field report with two
  Hebrew verses is comfortably fine; a long report could be truncated. Worth testing with a
  worst-case-length report before relying on it.
- **`\n` vs `\r\n`.** SMTP requires CRLF line endings. MailKit handles this. In `mailto:` use
  `%0D%0A`.

---

## 6. The credential problem — the honest version

Suppose you ship KitveiHakodesh with your own Gmail App Password baked in so reports "just send".

**What actually happens:**

- The App Password is a static 16-character string sitting in the shipped binary or a config file
  next to it. Anyone can find it — `strings` on the executable, a .NET decompiler (ILSpy/dnSpy
  reconstruct near-original C# from IL), or simply a network capture of the STARTTLS handshake with
  a local proxy. Obfuscation raises the effort from *seconds* to *an afternoon*; it does not make
  it secret. Assume any secret shipped to a user's machine is public.
- That App Password grants **send-as-you** on your mailbox. Whoever extracts it can send arbitrary
  mail from your address to anyone — not just to Sefaria. That is the real exposure; it is not
  limited to the feature you built.
- Every user's correction arrives at Sefaria from **your** address, signed with **your** name.
  Sefaria cannot tell users apart, cannot reply to the person who actually found the typo, and
  sees one address generating all traffic (see §7).
- Users can't see or review what was sent on their behalf — nothing lands in a Sent folder they
  own. For a report attributed to a person, that's a genuine problem, not a nitpick.

**Do not do this.** Not with obfuscation, not with the string XOR'd, not with it fetched from your
server at runtime (that just moves the extraction point to the network call).

### The three real alternatives

**(a) Each user configures their own Gmail in app settings.**
The user enables 2FA, generates their own App Password, pastes address + password into a settings
pane. The app stores it locally, encrypted — on Windows the right tool is **DPAPI**
(`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`), which ties the ciphertext to that
Windows user account, or Windows Credential Manager.

- *Good:* no shared secret, mail is genuinely from the user, quota is per-user (500/day each),
  Sefaria can reply to the actual finder, the user has it in their Sent folder.
- *Bad:* the setup is far beyond a typical user. Explaining "enable 2-Step Verification, then visit
  myaccount.google.com/apppasswords, generate a 16-character password" to a non-technical
  Hebrew-speaking seforim reader is a support burden you will regret. Realistically a single-digit
  percentage will complete it. Also breaks entirely for Advanced Protection users (§1) and for
  anyone not on Gmail.
- *Fair verdict:* correct and safe, but the funnel loss makes it a worse outcome than `mailto:`.

**(b) `mailto:` — the user's own default mail client.**
The app builds the four-field report, percent-encodes it, and opens
`mailto:corrections@sefaria.org?subject=…&body=…`. The user's mail client opens with everything
filled in; they press Send.

- *Good:* zero credentials, zero config, zero quota, zero external accounts, zero policy exposure.
  The mail is genuinely from the user, in their Sent folder, replies reach them. It matches
  Sefaria's own documented "email us" flow exactly. And it gives the user a **review step before an
  email goes out in their name** — which is arguably a feature, not a compromise.
- *Bad:* not silent — one extra click. Requires a configured mail client; a user who reads mail only
  at `mail.google.com` in a browser may have no `mailto:` handler registered, in which case the
  click appears to do nothing. **Mitigation:** always show the composed report in a copyable text
  box alongside the button, with `corrections@sefaria.org` shown as selectable text, so the
  fallback is "copy and paste" rather than a dead end.
- *Fair verdict:* the right default for a shipped app.

**(c) A third-party relay** (EmailJS, Resend, Web3Forms, or your own small endpoint).
The app POSTs the report over HTTPS; the service sends the mail.

- *Good:* genuinely silent and one-click. No Gmail credential anywhere. Frontend can call it
  directly, so no C# work.
- *Bad:* the API key is still shipped and still extractable — the difference is that its blast
  radius is "can send mail through your relay quota" rather than "owns your mailbox", and you can
  rotate or revoke it without touching your personal account. Free tiers are capped per month and
  change without notice; verify current limits before relying on any published number. The mail
  originates from the relay's infrastructure or your connected mailbox, not the user's, so the
  attribution and reply-to problem from §6 remains. And you are now operating mail infrastructure:
  when someone abuses the extracted key, the deliverability damage and the account suspension are
  yours.
- *Fair verdict:* the only path to genuinely silent sending in a shipped app, and it costs you an
  external account, a recurring quota worry, and abuse liability. Justified only if silent send is
  non-negotiable.

---

## 7. Deliverability and anti-abuse — the funnel problem

**Partly documented, partly reasoned. Marked accordingly.**

**Documented:** exceeding the sending limit blocks sending for up to 24 hours (§3). Gmail also
throttles or blocks accounts when a high proportion of messages bounce or fail. Google's Terms of
Service prohibit using an account to send bulk or unsolicited mail, and Google reserves the right
to suspend accounts for abuse.

**Reasoned inference — but the mechanism is well understood:** one Gmail account sending
programmatically on behalf of many strangers looks, from Gmail's and from any receiving server's
perspective, exactly like a compromised account being used as a spam relay. The specific signals:

- **Volume shape.** A personal account that normally sends a handful of mails a day suddenly emits
  a steady programmatic stream, always to the same external recipient, all structurally identical.
  This is precisely the pattern anti-abuse systems are trained on.
- **Content uniformity.** Hundreds of near-identical templated messages to one address.
- **One recipient, repeatedly.** All traffic to `corrections@sefaria.org`. If Sefaria's staff start
  marking them (or if they're perceived as automated noise), your sender reputation degrades fast.
- **No unsubscribe, no consent signal.** Not applicable to genuine correction reports, but it means
  the mail has no legitimizing bulk-mail metadata either.

**Likely consequences, in escalating order:** messages start landing in Sefaria's spam folder →
Gmail issues temporary send blocks → a security challenge / forced re-verification on the account →
in a sustained case, account suspension. Losing the Gmail account means losing whatever else is on
it, which for a personal account is usually a lot.

**There's also a Sefaria-side cost.** The companion doc is explicit that these reports are read and
acted on **by a person**. A pipe that lets many users fire off unvetted reports at one human inbox
degrades the thing you're trying to help with. This is an argument for a review step in the UI
regardless of transport — which `mailto:` provides for free.

---

## 8. Comparison table

| Option | Automatic? | Free? | Per-user setup | Secret shipped? | Limits | Ships to users? |
|---|---|---|---|---|---|---|
| **`mailto:`** | No — user clicks Send in their own mail app | Yes, $0 | None | None | None | **Yes — recommended** |
| **Gmail SMTP, your App Password baked in** | Yes | Yes | None | **Yes — extractable, grants send-as-you** | 500/day shared across all users | **No — do not ship** |
| **Gmail SMTP, per-user App Password** | Yes, after setup | Yes | Heavy: enable 2FA + generate a 16-char password | No | 500/day per user | Technically yes; realistically almost nobody completes setup |
| **Gmail API OAuth, Testing mode** | Yes, after browser consent | Yes | Browser sign-in — **repeated every 7 days** | Client secret (public by design for desktop clients — acceptable) | 100 test users max; 7-day refresh token | **No — the weekly re-consent kills it** |
| **Gmail API OAuth, verified/published** | Yes, after one browser consent | Yes (no fee for sensitive scopes) | One-time browser sign-in | Same, acceptable | Gmail per-user quotas | Yes — but needs a domain, privacy policy, homepage, demo video, and a 3–5 business-day review |
| **Third-party relay (EmailJS etc.)** | Yes, silent | Free tier, capped — verify current limits | None | Yes — but revocable, limited blast radius | Monthly cap; changes without notice | Yes, if you accept running mail infrastructure |

---

## 9. Verdict

**Personal-use build:** Gmail SMTP + your own App Password + MailKit on the C# side. Store the
credential in a local config file protected with Windows DPAPI (`DataProtectionScope.CurrentUser`),
**never in source and never in the repo**. Five minutes of setup, free, no policy exposure, and the
500/day cap is unreachable for one person sending one report per typo.

**Shipped build:** `mailto:`. It is the only option that is simultaneously free, zero-setup,
zero-secret, unlimited, correctly attributed to the actual user, and reviewable by that user before
it leaves. The single extra click is a smaller cost than every alternative's downside — and given
that a human at Sefaria reads each of these, the review step is arguably the right behaviour anyway.
Pair the button with a copyable text box showing the composed report, so users without a registered
`mailto:` handler have a working fallback.

**Only if silent send in a shipped build becomes a hard requirement:** a relay you operate, with a
per-install rate limit and a revocable key. Not Gmail. Do not ship your own Gmail App Password
under any circumstances.

**Gmail API OAuth is the technically correct "each user sends as themselves" answer**, and it's
free — but the verification obligations (owned domain, privacy policy, public homepage, demo video,
recurring review) are real product overhead for one button. Revisit it only if KitveiHakodesh
becomes a properly published product with a website behind it.

---

## Sources

- Sefaria, [How to Report a Mistake](https://developers.sefaria.org/docs/how-to-report-a-mistake)
- Google, [Sign in with app passwords](https://support.google.com/accounts/answer/185833)
- Google, [Advanced Protection Program — common questions](https://support.google.com/accounts/answer/7539956)
- Google, [Gmail sending limits (consumer)](https://support.google.com/mail/answer/22839)
- Google, [Gmail sending limits in Google Workspace](https://knowledge.workspace.google.com/admin/gmail/gmail-sending-limits-in-google-workspace)
- Google, [Sensitive scope verification](https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification)
- Google, [Restricted scope verification](https://developers.google.com/identity/protocols/oauth2/production-readiness/restricted-scope-verification)
- Google, [Using OAuth 2.0 to Access Google APIs](https://developers.google.com/identity/protocols/oauth2) (7-day refresh token in Testing status)
- Google, [Manage App Audience](https://support.google.com/cloud/answer/15549945) (100 test users)
- Microsoft, [SmtpClient Class](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient) ("not recommended for new development… Use MailKit")
- Microsoft, [DE0005: SmtpClient shouldn't be used](https://github.com/dotnet/platform-compat/blob/master/docs/DE0005.md)
- [MailKit](https://github.com/jstedfast/MailKit)

*Checked September 2026. Google's OAuth verification policy and free-tier limits on third-party
relays change without much notice — re-verify §2 and the relay row of §8 before acting on them.*
