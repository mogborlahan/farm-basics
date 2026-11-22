import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createTrustOverride,
  fetchReviewQueue,
  submitReviewVerdict
} from "../api/client";

interface ReviewTrustWorkspaceProps {
  accessToken: string;
}

export function ReviewTrustWorkspace({ accessToken }: ReviewTrustWorkspaceProps) {
  const queryClient = useQueryClient();
  const [selectedVerdict, setSelectedVerdict] = useState<string>("APPROVE_IDENTITY");
  const [verdictNotes, setVerdictNotes] = useState<string>("");
  const [verdictFeedback, setVerdictFeedback] = useState<string | null>(null);

  const [targetIdentityId, setTargetIdentityId] = useState<string>("");
  const [overriddenTier, setOverriddenTier] = useState<"unverified" | "verified" | "trusted">("verified");
  const [reason, setReason] = useState<string>("");
  const [expiryDate, setExpiryDate] = useState<string>("");
  const [isPermanent, setIsPermanent] = useState<boolean>(false);
  const [overrideFeedback, setOverrideFeedback] = useState<string | null>(null);

  const queueQuery = useQuery({
    queryKey: ["review-queue", accessToken],
    queryFn: () => fetchReviewQueue(accessToken),
    enabled: Boolean(accessToken),
    retry: false
  });

  const verdictMutation = useMutation({
    mutationFn: ({ itemId, verdict, notes }: { itemId: string; verdict: string; notes: string }) =>
      submitReviewVerdict(accessToken, itemId, verdict, notes),
    onSuccess: (res) => {
      setVerdictFeedback(`Recorded ${res.verdictCode} (${res.status}).`);
      void queryClient.invalidateQueries({ queryKey: ["review-queue"] });
    },
    onError: (err: Error) => {
      setVerdictFeedback(err.message);
    }
  });

  const overrideMutation = useMutation({
    mutationFn: () =>
      createTrustOverride(accessToken, {
        identityId: targetIdentityId.trim(),
        overriddenTier,
        reason: reason.trim(),
        expiryDate: isPermanent ? undefined : expiryDate.trim(),
        isPermanent
      }),
    onSuccess: (res) => {
      setOverrideFeedback(`Trust override ${res.overrideId} applied (${res.overriddenTier}).`);
    },
    onError: (err: Error) => {
      setOverrideFeedback(err.message);
    }
  });

  const handleSubmitOverride = () => {
    setOverrideFeedback(null);
    if (!targetIdentityId.trim() || !reason.trim()) {
      setOverrideFeedback("Target Identity ID and justification reason are required.");
      return;
    }

    if (!isPermanent && !expiryDate.trim()) {
      setOverrideFeedback("Specify an Expiry Date or mark Permanent Override (BR-D-04).");
      return;
    }

    overrideMutation.mutate();
  };

  return (
    <div className="space-y-6 mt-6">
      <section className="rounded-xl border border-ink-150 bg-white p-6">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h2 className="text-lg font-bold">Review Operations Queue</h2>
            <p className="text-sm text-ink-700 mt-1">
              Claimed reviewer queue with configurable verdict write-back (`IDENTITY_NIN_MISMATCH`, Rule 8 &amp; P16).
            </p>
          </div>
          <div className="flex items-center gap-2">
            <label htmlFor="verdict-select" className="text-xs font-semibold text-ink-700">
              Verdict:
            </label>
            <select
              id="verdict-select"
              value={selectedVerdict}
              onChange={(e) => setSelectedVerdict(e.target.value)}
              className="rounded-lg border border-ink-150 bg-white px-3 py-2 text-sm"
            >
              <option value="APPROVE_IDENTITY">APPROVE_IDENTITY</option>
              <option value="REQUEST_CORRECTION">REQUEST_CORRECTION</option>
              <option value="REJECT_IDENTITY">REJECT_IDENTITY</option>
            </select>
            <input
              type="text"
              placeholder="Reviewer notes"
              value={verdictNotes}
              onChange={(e) => setVerdictNotes(e.target.value)}
              className="rounded-lg border border-ink-150 px-3 py-2 text-sm"
            />
          </div>
        </div>

        {!accessToken && (
          <p className="mt-4 text-sm text-ink-500">
            Sign in with Keycloak to load live review queue items.
          </p>
        )}

        {verdictFeedback && (
          <p role="status" className="mt-3 text-sm font-semibold text-forest">
            {verdictFeedback}
          </p>
        )}

        {accessToken && queueQuery.isPending && (
          <p className="mt-4 text-sm text-ink-500">Loading review queue...</p>
        )}

        {queueQuery.data && queueQuery.data.length === 0 && (
          <p className="mt-4 text-sm text-ink-500">No review items queued.</p>
        )}

        {queueQuery.data && queueQuery.data.length > 0 && (
          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-left text-sm border-collapse">
              <thead>
                <tr className="border-b border-ink-150 text-ink-500">
                  <th className="py-2 pr-3">Category</th>
                  <th className="py-2 pr-3">Subject</th>
                  <th className="py-2 pr-3">Priority</th>
                  <th className="py-2 pr-3">Status</th>
                  <th className="py-2">Action</th>
                </tr>
              </thead>
              <tbody>
                {queueQuery.data.map((item) => (
                  <tr key={item.id} className="border-b border-ink-150/60">
                    <td className="py-2.5 pr-3 font-mono text-xs">{item.categoryCode}</td>
                    <td className="py-2.5 pr-3 font-mono text-xs">{item.subjectRefId}</td>
                    <td className="py-2.5 pr-3">{item.priority}</td>
                    <td className="py-2.5 pr-3">{item.status}</td>
                    <td className="py-2.5">
                      <button
                        type="button"
                        disabled={verdictMutation.isPending}
                        onClick={() =>
                          verdictMutation.mutate({
                            itemId: item.id,
                            verdict: selectedVerdict,
                            notes: verdictNotes
                          })
                        }
                        className="rounded-lg bg-forest px-3 py-1.5 text-xs font-bold text-white hover:bg-sage"
                      >
                        Record Verdict
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="rounded-xl border border-ink-150 bg-white p-6">
        <h2 className="text-lg font-bold">Time-Boxed Trust Profile Override (`BR-D-04`)</h2>
        <p className="text-sm text-ink-700 mt-1">
          Every override must carry an expiry date unless explicitly marked permanent (`SuperAdmin` only).
        </p>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-4">
          <input
            type="text"
            aria-label="Target Identity ID"
            placeholder="Target Identity UUID"
            value={targetIdentityId}
            onChange={(e) => setTargetIdentityId(e.target.value)}
            className="rounded-lg border border-ink-150 px-3 py-2 text-sm"
          />
          <select
            aria-label="Overridden Trust Tier"
            value={overriddenTier}
            onChange={(e) =>
              setOverriddenTier(e.target.value as "unverified" | "verified" | "trusted")
            }
            className="rounded-lg border border-ink-150 bg-white px-3 py-2 text-sm"
          >
            <option value="verified">verified</option>
            <option value="trusted">trusted</option>
            <option value="unverified">unverified</option>
          </select>
          <input
            type="date"
            aria-label="Expiry Date"
            value={expiryDate}
            disabled={isPermanent}
            onChange={(e) => setExpiryDate(e.target.value)}
            className="rounded-lg border border-ink-150 px-3 py-2 text-sm"
          />
          <input
            type="text"
            aria-label="Override Reason"
            placeholder="Audit justification reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            className="rounded-lg border border-ink-150 px-3 py-2 text-sm"
          />
        </div>

        <div className="flex flex-wrap items-center justify-between gap-4 mt-4">
          <label className="inline-flex items-center gap-2 text-sm text-ink-700">
            <input
              type="checkbox"
              checked={isPermanent}
              onChange={(e) => setIsPermanent(e.target.checked)}
            />
            Permanent override (SuperAdmin only)
          </label>
          <button
            type="button"
            disabled={overrideMutation.isPending}
            onClick={handleSubmitOverride}
            className="min-h-12 px-5 rounded-full bg-forest text-white text-sm font-bold hover:bg-sage"
          >
            Apply Trust Override
          </button>
        </div>

        {overrideFeedback && (
          <p role="alert" className="mt-3 text-sm font-semibold text-clay">
            {overrideFeedback}
          </p>
        )}
      </section>
    </div>
  );
}
