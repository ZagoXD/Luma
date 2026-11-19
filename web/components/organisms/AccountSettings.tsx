"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Trash2, Undo2 } from "lucide-react";

type Order = { id: string; planCode: string; billingInterval: string; deadline?: string; withdrawalRequestedAt?: string; withdrawalCompletedAt?: string };
async function request(path: string, body?: unknown) {
  const response = await fetch(`/api/account/${path}`, body === undefined ? { cache: "no-store" } : { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  const data = await response.json();
  if (!response.ok) throw new Error(data.message || "Não foi possível concluir. Tente novamente ou procure o suporte.");
  return data;
}

export function AccountSettings({ accountId, onBillingChange }: { accountId: string; onBillingChange(): Promise<void> }) {
  const router = useRouter();
  const [orders, setOrders] = useState<Order[]>([]);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [password, setPassword] = useState("");
  const [now] = useState(() => Date.now());
  const load = () => request("subscription/withdrawals").then(data => setOrders(data.orders));
  useEffect(() => { void load().catch(error => setMessage(error.message)); }, []);
  async function withdraw(order: Order) {
    if (!window.confirm("Exercer o arrependimento e solicitar reembolso integral? Seu acesso será encerrado e as próximas cobranças serão interrompidas. O crédito na fatura depende do emissor.")) return;
    setBusy(true);
    try { await request("subscription/withdraw", { id: order.id }); setMessage("Reembolso confirmado pelo provedor. Acompanhe o crédito na sua fatura."); await load(); await onBillingChange(); router.refresh(); }
    catch (error) { setMessage(error instanceof Error ? error.message : "Pedido não confirmado."); await load(); await onBillingChange(); }
    finally { setBusy(false); }
  }
  return <section className="account-panel profile-card account-settings" id="configuracoes">
    <h2>Configurações da conta</h2>
    <p><Link href="/termos">Termos de Uso</Link> · <Link href="/privacidade">Política de Privacidade</Link></p>
    <h3>Arrependimento e reembolso</h3>
    <p>Você pode solicitar o reembolso integral no prazo legal de sete dias. Esta ação é diferente de cancelar a assinatura ou desativar a renovação.</p>
    {orders.length === 0 && <p>Nenhuma contratação para reembolso.</p>}
    {orders.map(order => {
      const valid = !!order.deadline && Date.parse(order.deadline) >= now;
      return <div className="settings-order" key={order.id}>
        <strong>{order.planCode === "essencial" ? "Essencial" : "Básico"} · {order.billingInterval === "annual" ? "Anual" : "Mensal"}</strong>
        <p>{order.withdrawalCompletedAt ? "Reembolso confirmado pelo provedor." : order.withdrawalRequestedAt ? "Pedido registrado; processamento pendente." : !order.deadline ? "Sem pagamento aprovado." : valid ? `Prazo da solicitação: ${new Date(order.deadline).toLocaleString("pt-BR")}.` : "O prazo automático de sete dias terminou. Outros pedidos podem ser enviados ao suporte."}</p>
        {!order.withdrawalCompletedAt && (valid || order.withdrawalRequestedAt) && <button type="button" className="account-secondary" disabled={busy} onClick={() => void withdraw(order)}><Undo2 size={16} />{order.withdrawalRequestedAt ? "Consultar e tentar concluir reembolso" : "Solicitar arrependimento e reembolso"}</button>}
      </div>;
    })}
    <Link href={`/perfil/${accountId}/suporte`}>Falar com o suporte</Link>
    <h3>Excluir conta</h3>
    <p>A exclusão interrompe as cobranças, encerra o acesso e elimina os dados pessoais e de saúde conforme a política de privacidade. Ela não solicita reembolso automaticamente.</p>
    {!deleting ? <button type="button" className="account-secondary danger" onClick={() => setDeleting(true)}><Trash2 size={16} />Excluir minha conta</button> : <form onSubmit={async event => {
      event.preventDefault(); setBusy(true);
      try { const result = await request("delete", { password }); setMessage(result.message); window.alert(result.message); router.replace("/login"); router.refresh(); }
      catch (error) { setMessage(error instanceof Error ? error.message : "Exclusão não confirmada."); }
      finally { setBusy(false); }
    }}><label>Confirme sua senha<input type="password" autoComplete="current-password" required value={password} onChange={e => setPassword(e.target.value)} /></label><p>Esta ação não pode ser desfeita.</p><button type="submit" className="account-secondary danger" disabled={busy}>Confirmar exclusão da conta</button><button type="button" className="account-secondary" disabled={busy} onClick={() => { setDeleting(false); setPassword(""); }}>Voltar</button></form>}
    {message && <p role="status" className="account-status info">{message}</p>}
  </section>;
}
