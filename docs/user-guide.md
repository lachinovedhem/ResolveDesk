# User guide

For the people who work the queue: agents, and the coordinator who routes to them.

## What this is for

A ticket arrives from a phone call, an email or the portal. Somebody has almost certainly solved it
before — perhaps eighteen months ago, perhaps by a colleague who has since left. ResolveDesk's job is
to put that earlier answer in front of you before you start investigating.

Everything the model produces is **advice**. Nothing it says is written onto the ticket, and every
number it produces is shown with the reasoning behind it, so you can disagree with it.

## Signing in

The sign-in screen shows whatever your deployment has turned on — a password, a passkey (Face ID,
Touch ID, Windows Hello, or a security key), single sign-on, or several at once. If your account has
two-factor authentication enrolled, a password gets you a six-digit code prompt rather than a session.

You will not find a sign-up form. Accounts are created by a coordinator or an administrator, and you
receive a link that lets you set your own password. **That link works once.** If you open it twice, or
forward it and someone else opens it first, it stops working and you need a new one — which is the
point of it.

## The queue

On a desktop the ticket list is a grid: sort any column, filter in the header row, and page through
with the keyboard. On a phone the same list becomes cards, because a data grid on a small screen is a
data grid nobody reads.

Status is stage — Open, Assigned, In progress, Waiting on customer, Resolved, Closed — and takes cool
colours. Priority is urgency and takes warm ones. The two scales are kept apart deliberately, so a
glance at a row separates "where is this" from "how bad is this".

## A ticket, and what the archive says about it

Open any ticket and the panel below the description is the reason this product exists.

**From past resolutions** lists the closest tickets the team has already closed, each with what was
actually done. The badge on each one says how it was found:

| Badge | Meaning |
|---|---|
| `meaning + keyword` | Found by both the vector search and the text search — the strongest signal |
| `meaning` | Found by similarity, even though the wording differs |
| `keyword` | Found by shared terms |

The percentage next to a semantic match is a similarity. Next to a keyword-only match you will see a
relevance figure instead, and it is not the same scale — a small number there can still be a decisive
first place. That is why the two are labelled differently.

**Drafted answer** is written from those matches and cites them by reference code. It says so above
the text: *generated from the matches below — verify before sending to a customer.* Treat it as a
first draft by someone who has read the archive, not as an answer.

**Assessment** sizes the ticket: difficulty 1–5, hands-on minutes, a suggested category and priority,
and whether it looks like a duplicate of something already open. The confidence is shown, not hidden —
a low number is the signal to ignore the rest of the panel.

**Who should take this** ranks the team on three numbers you can see: how many similar tickets each
person has resolved, how many of their skills appear in the ticket text, and how much is already on
their plate. When none of those signals exist, it says so plainly rather than presenting "least busy"
as a match. Re-run it after the team changes.

## Resolving one

Write the resolution the way you would write a note to yourself — shorthand is fine. On save it is
tidied into two things: a reply you can send to the customer, and a clean summary for the archive.
**Your original text is never modified.**

At the same time the resolution is compared with how the team has solved the same thing before:

| Verdict | Meaning |
|---|---|
| Consistent | Same approach as past resolutions |
| Differs | Same result by a different route — worth a look |
| Novel | Nothing comparable; this is new knowledge |
| Conflicts | Contradicts a past resolution — coordinators are told |

That last one is the point of the whole exercise. Two agents quietly solving the same problem in
contradictory ways is expensive, and it is invisible without something looking for it.

## Searching the archive directly

**Knowledge** searches past resolutions without needing a ticket — useful when a customer is on the
phone and you want to check something before promising anything.

## Roles

| Role | Can |
|---|---|
| **Agent** | Work tickets, comment, resolve |
| **Coordinator** | All of that, plus assign tickets and create accounts |
| **Admin** | All of that, plus manage roles |

## Your own security settings

Under **Settings** you manage your own credentials.

**Passkeys** let you sign in with your device instead of a password. The key never leaves the device,
and because the signature is tied to this site's address, a passkey cannot be phished onto a lookalike
page the way a password can. Add one per device you use.

**Two-factor authentication** asks for a six-digit code from an authenticator app in addition to your
password. Enrolment takes two steps on purpose: it is not switched on until you have entered a code
that proves the app really holds the secret. Turning it off needs a current code too, so somebody who
walks up to an unlocked laptop cannot simply remove it.

## When something is missing

If the assessment or the drafted answer does not appear, the deployment may have no chat model
configured, or the background worker may still be running — on a local model that takes tens of
seconds. If matches are labelled `keyword` only, the semantic half is not available. Either way the
ticket, the queue and the archive all work exactly as before; the retrieval degrades rather than
failing.
