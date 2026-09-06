import { useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, Field, Input, PageHeader, Select, Textarea } from "@/components/ui/primitives";
import { SuggestionPanel } from "@/features/ai/SuggestionPanel";
import { useI18n } from "@/i18n";
import { useDebounced } from "@/lib/hooks";
import {
  api, TICKET_PRIORITIES, TICKET_SOURCES,
  type SuggestionResult, type TicketPriority, type TicketSource,
} from "@/lib/api";

/**
 * Intake with the knowledge base attached. Past resolutions are searched while the operator is still
 * typing, so a known problem can be answered on the call instead of becoming a ticket that waits.
 */
export function NewTicketPage() {
  const { t } = useI18n();
  const navigate = useNavigate();

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [priority, setPriority] = useState<TicketPriority>("Normal");
  const [source, setSource] = useState<TicketSource>("Phone");
  const [category, setCategory] = useState("");
  const [customerName, setCustomerName] = useState("");
  const [customerContact, setCustomerContact] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [suggestions, setSuggestions] = useState<SuggestionResult | null>(null);
  const [searching, setSearching] = useState(false);

  // Debounced so a request is not fired per keystroke, and short fragments are skipped entirely —
  // they retrieve noise and cost a model call.
  const query = useDebounced(`${title} ${description}`.trim(), 600);

  useEffect(() => {
    if (query.length < 12) {
      setSuggestions(null);
      return;
    }
    let current = true;
    setSearching(true);
    api.searchKnowledge(title, description, 4)
      .then((result) => { if (current) setSuggestions(result); })
      .catch(() => { if (current) setSuggestions(null); })
      .finally(() => { if (current) setSearching(false); });
    return () => { current = false; };
    // `query` is the debounced trigger; title/description are read at call time.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query]);

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const created = await api.createTicket({
        title, description, priority, source,
        category: category || undefined,
        customerName,
        customerContact: customerContact || undefined,
      });
      navigate(`/tickets/${created.id}`);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t("error.generic"));
      setBusy(false);
    }
  };

  return (
    <>
      <PageHeader title={t("new.title")} subtitle={t("new.subtitle")} />

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_26rem]">
        <Card>
          <CardBody>
            <form onSubmit={onSubmit} className="space-y-4">
              <Field label={t("ticket.title")} htmlFor="title">
                <Input id="title" required autoFocus value={title}
                       onChange={(event) => setTitle(event.target.value)} />
              </Field>

              <Field label={t("ticket.description")} htmlFor="description">
                <Textarea id="description" required value={description}
                          onChange={(event) => setDescription(event.target.value)} />
              </Field>

              <div className="grid gap-4 sm:grid-cols-2">
                <Field label={t("ticket.priority")} htmlFor="priority">
                  <Select id="priority" value={priority}
                          onChange={(event) => setPriority(event.target.value as TicketPriority)}>
                    {TICKET_PRIORITIES.map((value) => (
                      <option key={value} value={value}>{t(`priority.${value}`)}</option>
                    ))}
                  </Select>
                </Field>

                <Field label={t("ticket.source")} htmlFor="source">
                  <Select id="source" value={source}
                          onChange={(event) => setSource(event.target.value as TicketSource)}>
                    {TICKET_SOURCES.map((value) => (
                      <option key={value} value={value}>{t(`source.${value}`)}</option>
                    ))}
                  </Select>
                </Field>

                <Field label={t("ticket.customer")} htmlFor="customerName">
                  <Input id="customerName" required value={customerName}
                         onChange={(event) => setCustomerName(event.target.value)} />
                </Field>

                <Field label={t("ticket.contact")} htmlFor="customerContact">
                  <Input id="customerContact" value={customerContact}
                         onChange={(event) => setCustomerContact(event.target.value)} />
                </Field>
              </div>

              <Field label={t("ticket.category")} htmlFor="category" error={error}>
                <Input id="category" value={category}
                       onChange={(event) => setCategory(event.target.value)} />
              </Field>

              <Button type="submit" variant="primary" size="lg" loading={busy}>
                {t("new.submit")}
              </Button>
            </form>
          </CardBody>
        </Card>

        <SuggestionPanel result={suggestions} loading={searching} />
      </div>
    </>
  );
}
