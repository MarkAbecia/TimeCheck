import { NextResponse, type NextRequest } from "next/server";
import { fail, isSameOrigin, sessionFrom, signedOut, withSession } from "@/lib/server/bff";
import { gotrue } from "@/lib/server/gotrue";

/**
 * Starts TOTP for admins (§6.1). With a verified factor it just reports it; otherwise it drops any abandoned
 * unverified factor and enrols a fresh one, returning the QR code and the secret for manual entry.
 */
export async function POST(req: NextRequest) {
  if (!isSameOrigin(req)) return fail(403);
  const session = await sessionFrom(req);
  if (!session) return signedOut();

  const factors = await gotrue.factors(session.accessToken);
  if (!factors.ok) return factors.status === 401 ? signedOut() : withSession(fail(502), session);
  const totp = (factors.data.factors ?? []).filter((f) => f.factor_type === "totp");
  const verified = totp.find((f) => f.status === "verified");
  if (verified) return withSession(NextResponse.json({ factorId: verified.id, enrolled: true }), session);

  for (const f of totp) await gotrue.unenroll(session.accessToken, f.id);
  const enrolled = await gotrue.enrollTotp(session.accessToken);
  if (!enrolled.ok) return withSession(fail(502), session);
  return withSession(
    NextResponse.json(
      {
        factorId: enrolled.data.id,
        enrolled: false,
        qrCode: asDataUrl(enrolled.data.totp.qr_code),
        secret: enrolled.data.totp.secret,
      },
      { headers: { "Cache-Control": "no-store" } },
    ),
    session,
  );
}

// Older GoTrue returns a data: URL, newer returns the raw SVG document; the <img> needs a URL either way.
const asDataUrl = (qr: string) => (qr.startsWith("data:") ? qr : `data:image/svg+xml;charset=utf-8,${encodeURIComponent(qr)}`);
