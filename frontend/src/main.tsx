import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  ArrowRight,
  Building2,
  CheckCircle2,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ClipboardCheck,
  Clock3,
  Database,
  FileSpreadsheet,
  Leaf,
  LoaderCircle,
  RefreshCw,
  LogOut,
  Plus,
  Search,
  Settings2,
  ShieldCheck,
  SlidersHorizontal,
  Trash2,
  Users,
  X,
} from 'lucide-react';
import { api, ApiError, dateTime, json, resetCsrf, download } from './api';
import type { Branch, Overview, Survey, User, VisitPage, Question, Visit } from './types';
import CustomerPage, { EditVisitPage } from './CustomerPage';
import OfflineStatus from './OfflineStatus';
import QuestionEditor from './QuestionEditor';
import {session,setSession,allOutbox} from './offlineStore';
import VisitAnswers from './VisitAnswers';
import './styles.css';
import './reliability.css';
import {markVisitDeleted,markVisitRestored} from './visitLifecycle';
import DeleteVisitDialog from './DeleteVisitDialog';

const message = (error: unknown) =>
  error instanceof Error ? error.message : 'Παρουσιάστηκε σφάλμα.';
function Notice({ children, success = false }: { children: React.ReactNode; success?: boolean }) {
  return (
    <div role={success ? 'status' : 'alert'} className={`notice ${success ? 'success' : ''}`}>
      {success ? <CheckCircle2 size={18} /> : <span className="notice-mark">!</span>}
      <span>{children}</span>
    </div>
  );
}
function Spinner() {
  return (
    <div className="loading" role="status">
      <LoaderCircle className="spin" size={22} /> Φόρτωση…
    </div>
  );
}
function Brand() {
  return (
    <div className="brand">
      <span className="brand-icon">
        <Leaf size={23} />
      </span>
      <span>
        paper<span className="brand-dot">.</span>
      </span>
    </div>
  );
}
function Empty({
  icon = <Search size={29} />,
  title,
  children,
}: {
  icon?: React.ReactNode;
  title: string;
  children?: React.ReactNode;
}) {
  return (
    <div className="empty">
      <span className="empty-icon">{icon}</span>
      <h3>{title}</h3>
      <p>{children}</p>
    </div>
  );
}
function useRoute() {
  const [route, setRoute] = useState(window.location.hash.slice(1) || '/customers');
  useEffect(() => {
    const listener = () => setRoute(window.location.hash.slice(1) || '/customers');
    window.addEventListener('hashchange', listener);
    return () => window.removeEventListener('hashchange', listener);
  }, []);
  return route;
}

function Login({ setup, onLogin }: { setup: boolean; onLogin: (user: User) => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError('');
    const form = new FormData(event.currentTarget);
    try {
      const user = await api<User>(
        setup ? '/auth/setup' : '/auth/login',
        json({
          email: form.get('email'),
          password: form.get('password'),
          name: form.get('name'),
          role: 'Admin',
        }),
      );
      resetCsrf();
      setSession(user);
      onLogin(user);
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="login-page">
      <section className="login-story">
        <Brand />
        <div>
          <span className="eyebrow light">ΠΕΛΑΤΕΣ · ΑΝΘΡΩΠΟΙ · ΣΥΝΕΡΓΑΣΙΕΣ</span>
          <h1>
            Κάθε επίσκεψη,
            <br />
            μια καλύτερη
            <br />
            <em>εικόνα.</em>
          </h1>
          <p>Οι πελάτες σου, τα στοιχεία τους και οι επισκέψεις σου. Όλα σε έναν χώρο.</p>
        </div>
        <div className="story-footer">
          <span className="status-dot" /> Ένα απλό εργαλείο για την ομάδα σου.
        </div>
      </section>
      <section className="login-form-wrap">
        <form className="login-form" onSubmit={submit}>
          <div className="login-badge">
            <ShieldCheck size={25} />
          </div>
          <span className="eyebrow">{setup ? 'ΠΡΩΤΗ ΕΚΚΙΝΗΣΗ' : 'ΚΑΛΩΣ ΗΡΘΕΣ'}</span>
          <h2>{setup ? 'Δημιουργία διαχειριστή' : 'Σύνδεση στον χώρο σου'}</h2>
          <p>
            {setup
              ? 'Δημιούργησε τον πρώτο λογαριασμό από αυτόν τον υπολογιστή.'
              : 'Συνδέσου με τα στοιχεία του λογαριασμού σου.'}
          </p>
          {error && <Notice>{error}</Notice>}
          {setup && (
            <label>
              Ονοματεπώνυμο
              <input name="name" autoComplete="name" required maxLength={128} />
            </label>
          )}
          <label>
            Email
            <input
              name="email"
              type="email"
              autoComplete="username"
              required
              maxLength={254}
              placeholder="name@company.gr"
            />
          </label>
          <label>
            Κωδικός πρόσβασης
            <input
              name="password"
              type="password"
              autoComplete={setup ? 'new-password' : 'current-password'}
              required
              minLength={setup ? 12 : 1}
              maxLength={128}
            />
          </label>
          {setup && <small>Τουλάχιστον 12 χαρακτήρες, με γράμματα και αριθμούς.</small>}
          <button className="btn primary wide" disabled={busy}>
            {busy ? (
              <LoaderCircle className="spin" size={18} />
            ) : (
              <>
                {setup ? 'Δημιουργία λογαριασμού' : 'Σύνδεση'}
                <ArrowRight size={18} />
              </>
            )}
          </button>
          {!setup && (
            <p className="login-help">
              Για πρόσβαση στην εφαρμογή, επικοινώνησε με τον διαχειριστή.
            </p>
          )}
        </form>
      </section>
    </div>
  );
}

function CustomerSearch({
  go,
  overview,
}: {
  go: (path: string) => void;
  overview: Overview | null;
}) {
  const [tax, setTax] = useState(() => sessionStorage.getItem('last-tax') ?? '');
  const [results, setResults] = useState<Branch[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function search(event?: React.FormEvent) {
    event?.preventDefault();
    if (busy) return;
    if (!/^\d{9}$/.test(tax)) {
      setError('Συμπλήρωσε ένα ΑΦΜ με ακριβώς 9 ψηφία.');
      return;
    }
    setBusy(true);
    setError('');
    setResults(null);
    try {
      const rows = await api<Branch[]>(`/customers?afm=${tax}`);
      setResults(rows);
      sessionStorage.setItem('last-tax', tax);
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
    }
  }
  useEffect(() => {
    if (/^\d{9}$/.test(tax)) void search();
  }, []); // Restore the last search after opening a branch.
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">Ο ΧΩΡΟΣ ΕΡΓΑΣΙΑΣ ΣΟΥ</span>
          <h1>Πελάτες</h1>
          <p>Βρες τον πελάτη σου και οργάνωσε την επίσκεψή σου.</p>
        </div>
        <span className="pill subtle">
          <Database size={14} />
          {overview ? `${overview.customers.toLocaleString('el-GR')} πελάτες` : 'Φόρτωση βάσης…'}
        </span>
      </div>
      <section className="search-hero">
        <div className="hero-decoration">
          <Building2 size={134} strokeWidth={0.65} />
        </div>
        <span className="eyebrow">Η ΕΠΟΜΕΝΗ ΕΠΙΣΚΕΨΗ ΞΕΚΙΝΑ ΕΔΩ</span>
        <h2>Ποιον πελάτη αναζητάς;</h2>
        <p>Πληκτρολόγησε το ΑΦΜ για να βρεις όλα τα υποκαταστήματά του.</p>
        <form className="search-form" onSubmit={search}>
          <Search size={22} />
          <label className="sr-only" htmlFor="tax-search">
            ΑΦΜ πελάτη
          </label>
          <input
            id="tax-search"
            disabled={busy}
            inputMode="numeric"
            autoComplete="off"
            value={tax}
            onChange={(event) => {
              setTax(event.target.value.replace(/\D/g, '').slice(0, 9));
              setResults(null);
              setError('');
            }}
            placeholder="Αναζήτηση με 9ψήφιο ΑΦΜ"
            maxLength={9}
          />
          {tax && (
            <button
              type="button"
              className="icon-button"
              aria-label="Καθαρισμός αναζήτησης"
              disabled={busy}
              onClick={() => {
                setTax('');
                setResults(null);
                sessionStorage.removeItem('last-tax');
              }}
            >
              <X size={17} />
            </button>
          )}
          <button className="btn primary" disabled={busy}>
            {busy ? (
              <LoaderCircle size={18} className="spin" />
            ) : (
              <>
                Αναζήτηση
                <ArrowRight size={17} />
              </>
            )}
          </button>
        </form>
        <small>
          <ShieldCheck size={14} /> Τα υποκαταστήματα εμφανίζονται ως ξεχωριστές καρτέλες.
        </small>
      </section>
      {error && <Notice>{error}</Notice>}
      <section className="panel results-panel">
        <div className="section-heading">
          <div>
            <h2>
              Αποτελέσματα αναζήτησης{' '}
              {results && <span className="count-badge">{results.length}</span>}
            </h2>
            <p>
              {results
                ? `ΑΦΜ ${tax} · Επίλεξε την καρτέλα που θέλεις να ανοίξεις.`
                : 'Η σωστή καρτέλα, με μία αναζήτηση.'}
            </p>
          </div>
          <span className="small-label">ΚΑΡΤΕΛΕΣ ΠΕΛΑΤΩΝ</span>
        </div>
        {busy ? (
          <Spinner />
        ) : results === null ? (
          <Empty title="Αναζήτησε έναν πελάτη">
            Γράψε το ΑΦΜ στο πεδίο παραπάνω για να δεις τα διαθέσιμα υποκαταστήματα.
          </Empty>
        ) : results.length === 0 ? (
          <Empty title="Δεν βρέθηκε πελάτης">
            Δεν υπάρχει καταχωρισμένος πελάτης με αυτό το ΑΦΜ. Έλεγξε τα ψηφία και δοκίμασε ξανά.
          </Empty>
        ) : (
          <div className="customer-list">
            {results.map((branch, index) => (
              <button
                className="customer-row"
                key={branch.code}
                onClick={() => go(`/customers/${encodeURIComponent(branch.code)}`)}
              >
                <span className="customer-avatar">
                  <Building2 size={22} />
                </span>
                <span className="customer-row-main">
                  <strong>{branch.name}</strong>
                  <span>
                    <span className="mono">{branch.code}</span>
                    <i />
                    ΑΦΜ <span className="mono">{branch.taxId}</span>
                  </span>
                </span>
                <span className="branch-number">{String(index + 1).padStart(2, '0')}</span>
                <ArrowRight size={20} />
              </button>
            ))}
          </div>
        )}
      </section>
      <div className="bottom-info">
        <span>
          <FileSpreadsheet size={17} />
          Στοιχεία από την τελευταία εισαγωγή Excel
        </span>
        <span>
          {overview?.latestImport
            ? `Τζίρος ${overview.latestImport.year} · Ενημέρωση ${dateTime(overview.latestImport.createdAtUtc)}`
            : 'Δεν έχει εισαχθεί ακόμη αρχείο πελατών.'}
        </span>
      </div>
    </>
  );
}

function Visits({ user, go }: { user: User; go: (path: string) => void }) {
  const [data, setData] = useState<VisitPage | null>(null);
  const [users, setUsers] = useState<User[]>([]);
  const [filters, setFilters] = useState({ afm: '', sellerId: '', from: '', to: '' });
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState('');
  const [exporting,setExporting]=useState(false);
  const [exportScope,setExportScope]=useState('all');
  const [open, setOpen] = useState<string | null>(null);
  const [confirmDelete,setConfirmDelete]=useState<Visit|null>(null);
  const [deleting,setDeleting]=useState(false);
  const [deletedVisit,setDeletedVisit]=useState<Visit|null>(null);
  const [reload,setReload]=useState(0);
  async function removeVisit(visit:Visit){
    if(deleting)return;
    setDeleting(true);setError('');
    try{
      await api(`/visits/${visit.id}`,json({expectedVersion:visit.version},'DELETE'));
      setDeletedVisit(visit);setReload(value=>value+1);
      await markVisitDeleted(user,visit);
    }catch(error){setError(error instanceof ApiError?message(error):'Η διαγραφή δεν επιβεβαιώθηκε. Χρειάζεται σύνδεση. Πάτησε «Ανανέωση» για έλεγχο και δοκίμασε ξανά.');}
    finally{setDeleting(false);setConfirmDelete(null);}
  }
  async function undoDelete(){
    if(!deletedVisit||deleting)return;
    setDeleting(true);setError('');
    try{
      const restored=await api<Visit>(`/visits/${deletedVisit.id}/restore`,json({expectedVersion:deletedVisit.version+1}));
      setDeletedVisit(null);setReload(value=>value+1);await markVisitRestored(user,restored);
    }catch(error){setError(message(error));}finally{setDeleting(false);setConfirmDelete(null);}
  }
  useEffect(()=>{const saved=()=>setReload(value=>value+1);window.addEventListener('paper-visit-saved',saved);return()=>window.removeEventListener('paper-visit-saved',saved);},[]);
  useEffect(() => {
    if (user.role === 'Admin')
      api<User[]>('/admin/users')
        .then(setUsers)
        .catch((error) => setError(message(error)));
  }, [user.role]);
  useEffect(() => {
    let cancelled = false;
    setBusy(true);
    setError('');
    api<VisitPage>(`/visits?page=${page}${query}`)
      .then((data) => {
        if (!cancelled) setData(data);
      })
      .catch((error) => {
        if (!cancelled) setError(message(error));
      })
      .finally(() => {
        if (!cancelled) setBusy(false);
      });
    return () => {
      cancelled = true;
    };
  }, [query, page, user.id, reload]);
  function filter(event: React.FormEvent) {
    event.preventDefault();
    const params = new URLSearchParams();
    Object.entries(filters).forEach(([key, value]) => {
      if (value) params.set(key, value);
    });
    setPage(1);
    setQuery(params.size ? `&${params}` : '');
  }
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">
            {user.role === 'Admin' ? 'Η ΕΙΚΟΝΑ ΤΗΣ ΟΜΑΔΑΣ' : 'ΤΟ ΙΣΤΟΡΙΚΟ ΣΟΥ'}
          </span>
          <h1>{user.role === 'Admin' ? 'Επισκέψεις ομάδας' : 'Οι επισκέψεις μου'}</h1>
          <p>Κάθε επίσκεψη, με τις επιλογές και τις σημειώσεις της.</p>
        </div>
        <div className="heading-actions"><button className="btn secondary" disabled={busy} onClick={()=>setReload(value=>value+1)}><RefreshCw size={16}/>Ανανέωση</button>{user.role==='Admin'&&<><label className="export-scope">Περιεχόμενο Excel<select value={exportScope} disabled={exporting} onChange={event=>setExportScope(event.target.value)}><option value="all">Όλες οι επισκέψεις</option><option value="latest">Τελευταία ανά κατάστημα</option></select></label><button className="btn primary" disabled={exporting} onClick={async()=>{setExporting(true);try{await download(`/admin/visits/export?${query.replace(/^&/,'')}&latestOnly=${exportScope==='latest'}`,exportScope==='latest'?'επισκέψεις-τελευταία-ανά-κατάστημα':'επισκέψεις-όλες');}catch(error){setError(message(error));}finally{setExporting(false);}}}><FileSpreadsheet size={18}/>{exporting?'Εξαγωγή…':'Εξαγωγή Excel'}</button></>}<span className="pill subtle">
          <Clock3 size={14} />
          Ώρα Ελλάδας
        </span></div>
      </div>
      {confirmDelete&&<DeleteVisitDialog visit={confirmDelete} busy={deleting} onCancel={()=>setConfirmDelete(null)} onConfirm={()=>void removeVisit(confirmDelete)}/>}
      {deletedVisit&&<div className="delete-confirmation" role="status"><CheckCircle2 size={19}/><span>Η επίσκεψη στον πελάτη «{deletedVisit.customerName}» διαγράφηκε.</span><button type="button" className="btn secondary" disabled={deleting} onClick={()=>void undoDelete()}>Αναίρεση διαγραφής</button></div>}
      <div className="stats-grid">
        {[
          [ClipboardCheck, 'Καταγεγραμμένες επισκέψεις', data?.total],
          [Building2, 'Υποκαταστήματα με επίσκεψη', data?.customerCount],
          [Users, 'Πωλητές με καταγραφές', data?.sellerCount],
        ].map(([Icon, label, value]) => {
          const MetricIcon = Icon as typeof Users;
          return (
            <section className="panel stat" key={String(label)}>
              <span className="metric-icon">
                <MetricIcon size={21} />
              </span>
              <span>{String(label)}</span>
              <strong>{busy || error || !data ? '—' : (value ?? 0).toLocaleString('el-GR')}</strong>
              <small>Με τα επιλεγμένα φίλτρα</small>
            </section>
          );
        })}
      </div>
      <section className="panel">
        <form className="filters" onSubmit={filter}>
          <label>
            ΑΦΜ
            <input
              inputMode="numeric"
              value={filters.afm}
              maxLength={9}
              pattern="[0-9]{9}"
              placeholder="Όλοι οι πελάτες"
              onChange={(event) =>
                setFilters({ ...filters, afm: event.target.value.replace(/\D/g, '') })
              }
            />
          </label>
          {user.role === 'Admin' && (
            <label>
              Πωλητής
              <select
                value={filters.sellerId}
                onChange={(event) => setFilters({ ...filters, sellerId: event.target.value })}
              >
                <option value="">Όλοι οι χρήστες</option>
                {users.map((seller) => (
                  <option key={seller.id} value={seller.id}>
                    {seller.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label>
            Από
            <input
              type="date"
              value={filters.from}
              onChange={(event) => setFilters({ ...filters, from: event.target.value })}
            />
          </label>
          <label>
            Έως
            <input
              type="date"
              value={filters.to}
              min={filters.from}
              onChange={(event) => setFilters({ ...filters, to: event.target.value })}
            />
          </label>
          <button className="btn secondary" disabled={busy}>
            <SlidersHorizontal size={16} />
            Εφαρμογή
          </button>
          <button
            className="icon-button"
            type="button"
            title="Καθαρισμός φίλτρων"
            aria-label="Καθαρισμός φίλτρων"
            onClick={() => {
              setFilters({ afm: '', sellerId: '', from: '', to: '' });
              setQuery('');
              setPage(1);
            }}
          >
            <X size={18} />
          </button>
        </form>
        {error && <Notice>{error}</Notice>}
        {busy ? (
          <Spinner />
        ) : error ? null : !data?.items.length ? (
          <Empty icon={<ClipboardCheck size={28} />} title="Δεν βρέθηκαν επισκέψεις">
            Οι καταγραφές θα εμφανιστούν εδώ μετά την αποστολή τους. Μπορείς επίσης να αλλάξεις τα
            φίλτρα.
          </Empty>
        ) : (
          <>
            <div className="table-wrap">
              <table className="visits-table">
                <thead>
                  <tr>
                    <th>Πελάτης / Υποκατάστημα</th>
                    <th>Πωλητής</th>
                    <th>Ημερομηνία</th>
                    <th>Καταγραφή</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((visit) => (
                    <React.Fragment key={visit.id}>
                      <tr>
                        <td>
                          <button
                            className="text-button customer-name-link"
                            onClick={() => go(`/customers/${encodeURIComponent(visit.branchCode)}`)}
                          >
                            {visit.customerName}
                          </button>
                          <small className="table-subtitle">
                            <span className="mono">{visit.branchCode}</span> · ΑΦΜ {visit.taxId}
                          </small>
                        </td>
                        <td>{visit.sellerName}</td>
                        <td className="date-cell">{dateTime(visit.createdAtUtc)}</td>
                        <td>
                          <button
                            className="btn tiny secondary"
                            aria-expanded={open === visit.id}
                            onClick={() => setOpen(open === visit.id ? null : visit.id)}
                          >
                            Απαντήσεις
                            <ChevronDown size={14} />
                          </button><button className="btn tiny secondary" onClick={()=>go(`/edit/${visit.id}`)}>Επεξεργασία</button><button type="button" className="btn tiny danger" disabled={deleting} aria-label={`Διαγραφή επίσκεψης: ${visit.customerName} · ${dateTime(visit.createdAtUtc)}`} onClick={()=>setConfirmDelete(visit)}><Trash2 size={14}/>Διαγραφή</button><small className="table-subtitle">Έκδοση {visit.version}{visit.updatedAtUtc?` · Διορθώθηκε ${dateTime(visit.updatedAtUtc)}`:''}</small>
                        </td>
                      </tr>
                      {open === visit.id && (
                        <tr className="expanded-row">
                          <td colSpan={4}>
                            <VisitAnswers visit={visit} />
                          </td>
                        </tr>
                      )}
                    </React.Fragment>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="pagination">
              <span>
                {data.total.toLocaleString('el-GR')} επισκέψεις · Σελίδα {page} από{' '}
                {Math.max(1, Math.ceil(data.total / data.pageSize))}
              </span>
              <div>
                <button
                  className="icon-button"
                  aria-label="Προηγούμενη σελίδα"
                  disabled={page === 1}
                  onClick={() => setPage(page - 1)}
                >
                  <ChevronLeft size={20} />
                </button>
                <button
                  className="icon-button"
                  aria-label="Επόμενη σελίδα"
                  disabled={page * data.pageSize >= data.total}
                  onClick={() => setPage(page + 1)}
                >
                  <ChevronRight size={20} />
                </button>
              </div>
            </div>
          </>
        )}
      </section>
    </>
  );
}

function Settings({ user, refresh }: { user: User; refresh: () => void }) {
  const [editing,setEditing]=useState<Question|null>(null);
  const [users, setUsers] = useState<User[]>([]);
  const [survey, setSurvey] = useState<Survey | null>(null);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [busy, setBusy] = useState(false);
  async function load() {
    const [people, questions] = await Promise.all([
      api<User[]>('/admin/users'),
      api<Survey>('/survey'),
    ]);
    setUsers(people);
    setSurvey(questions);
  }
  useEffect(() => {
    void load().catch((error) => setError(message(error)));
  }, []);
  async function action(work: () => Promise<void>) {
    setError('');
    setSuccess('');
    setBusy(true);
    try {
      await work();
      await load();
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      {editing&&survey&&<QuestionEditor question={editing} survey={survey} onClose={()=>setEditing(null)} onSaved={()=>{setEditing(null);setSuccess('Η ερώτηση ενημερώθηκε για νέες επισκέψεις.');void load().catch(error=>setError(message(error)));}}/>}
      <div className="page-heading">
        <div>
          <span className="eyebrow">ΔΙΑΧΕΙΡΙΣΗ ΕΦΑΡΜΟΓΗΣ</span>
          <h1>Ρυθμίσεις</h1>
          <p>Δεδομένα πελατών, πρόσβαση ομάδας και ερωτηματολόγιο.</p>
        </div>
        <span className="pill">
          <ShieldCheck size={14} />
          Διαχειριστής
        </span>
      </div>
      {error && <Notice>{error}</Notice>}
      {success && <Notice success>{success}</Notice>}
      <section className="panel settings-panel">
        <div className="section-heading">
          <div>
            <h2>
              <FileSpreadsheet size={21} />
              Εισαγωγή πελατών
            </h2>
            <p>Ενημέρωση πελατών και τζίρου από αρχείο Excel.</p>
          </div>
        </div>
        <form
          className="import-form"
          onSubmit={(event) => {
            event.preventDefault();
            const form = event.currentTarget;
            const data = new FormData(form);
            if (
              !window.confirm(
                'Η εισαγωγή αντικαθιστά όλο τον τζίρο του επιλεγμένου έτους. Οι επισκέψεις και τα άλλα έτη διατηρούνται. Συνέχεια;',
              )
            )
              return;
            void action(async () => {
              const result = await api<{ branches: number; turnoverRows: number; year: number }>(
                '/admin/import',
                { method: 'POST', body: data },
              );
              setSuccess(
                `Εισήχθησαν ${result.branches} πελάτες και ${result.turnoverRows} γραμμές τζίρου για το ${result.year}.`,
              );
              form.reset();
              refresh();
            });
          }}
        >
          <label className="file-field">
            Αρχείο .xlsx
            <input name="file" type="file" accept=".xlsx" required />
          </label>
          <label>
            Έτος τζίρου
            <input type="number" name="year" min={2000} max={2100} defaultValue={2026} required />
          </label>
          <button className="btn primary" disabled={busy}>
            <Database size={17} />
            Εισαγωγή
          </button>
        </form>
        <p className="helper">
          Πλήρες αρχείο για το επιλεγμένο έτος, έως 8 MB. Τα κενά σε κατηγορία και τζίρο σημαίνουν
          μηδενικές αγορές.
        </p>
      </section>
      <section className="panel settings-panel">
        <div className="section-heading">
          <div>
            <h2>
              <Users size={21} />
              Λογαριασμοί ομάδας
            </h2>
            <p>Κάθε χρήστης συνδέεται με προσωπικό λογαριασμό.</p>
          </div>
          <span className="count-badge">{users.length}</span>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Χρήστης</th>
                <th>Ρόλος</th>
                <th>Κατάσταση</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {users.map((person) => (
                <tr key={person.id}>
                  <td>
                    <strong>{person.name}</strong>
                    <small className="table-subtitle">{person.email}</small>
                  </td>
                  <td>{person.role === 'Admin' ? 'Διαχειριστής' : 'Πωλητής'}</td>
                  <td>
                    <span className={`pill ${person.active ? '' : 'subtle'}`}>
                      <span className={`status-dot ${person.active ? '' : 'inactive'}`} />
                      {person.active ? 'Ενεργός' : 'Ανενεργός'}
                    </span>
                  </td>
                  <td>
                    {person.id !== user.id && (
                      <button
                        className="text-button"
                        disabled={busy}
                        onClick={() =>
                          void action(async () => {
                            await api(
                              `/admin/users/${person.id}`,
                              json({ active: !person.active }, 'PATCH'),
                            );
                            setSuccess('Η κατάσταση λογαριασμού ενημερώθηκε.');
                          })
                        }
                      >
                        {person.active ? 'Απενεργοποίηση' : 'Ενεργοποίηση'}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <form
          className="new-user-form"
          onSubmit={(event) => {
            event.preventDefault();
            const form = event.currentTarget;
            const data = new FormData(form);
            void action(async () => {
              await api('/admin/users', json(Object.fromEntries(data)));
              form.reset();
              setSuccess('Ο νέος λογαριασμός δημιουργήθηκε.');
            });
          }}
        >
          <h3>Νέος λογαριασμός</h3>
          <div className="input-grid">
            <label>
              Ονοματεπώνυμο
              <input name="name" maxLength={128} required />
            </label>
            <label>
              Email
              <input name="email" type="email" maxLength={254} required autoComplete="off" />
            </label>
            <label>
              Κωδικός
              <input
                name="password"
                type="password"
                minLength={12}
                maxLength={128}
                required
                autoComplete="new-password"
                placeholder="12+ χαρακτήρες, γράμματα και αριθμοί"
              />
            </label>
            <label>
              Ρόλος
              <select name="role" defaultValue="Seller">
                <option value="Seller">Πωλητής</option>
                <option value="Admin">Διαχειριστής</option>
              </select>
            </label>
          </div>
          <button className="btn secondary" disabled={busy}>
            <Plus size={17} />
            Δημιουργία λογαριασμού
          </button>
        </form>
      </section>
      <section className="panel settings-panel">
        <div className="section-heading">
          <div>
            <h2>
              <ClipboardCheck size={21} />
              Ερωτηματολόγιο επίσκεψης
            </h2>
            <p>Κοινές ερωτήσεις ανά ενότητα, με μία ή πολλαπλές επιλογές.</p>
          </div>
        </div>
        {!survey ? (
          <Spinner />
        ) : survey.questions.length ? (
          survey.questions.map((question) => (
            <div className="question-setting" key={question.id}>
              <div>
                <strong>{question.title}</strong>
                <p className="helper">
                  {question.sectionTitle || 'Ανεξάρτητη ερώτηση'} ·{' '}
                  {question.selectionMode === 'Single' ? 'Μία επιλογή' : 'Πολλαπλές επιλογές'}
                </p>
                {question.required && <span className="small-label"> · Υποχρεωτική</span>}
                <div className="tags">
                  {question.options.map((option) => (
                    <span key={option.id} className="tag">
                      {option.label}
                    </span>
                  ))}
                </div>
              </div>
              <button className="btn tiny secondary" disabled={busy} onClick={()=>setEditing(question)}>Επεξεργασία</button>
              <button
                className="icon-button"
                aria-label={`Αρχειοθέτηση: ${question.title}`}
                disabled={busy}
                onClick={() => {
                  if (
                    window.confirm('Να αρχειοθετηθεί η ερώτηση; Οι παλιές απαντήσεις διατηρούνται.')
                  )
                    void action(async () => {
                      await api(`/admin/questions/${question.id}`, { method: 'DELETE' });
                      setSuccess('Η ερώτηση αρχειοθετήθηκε.');
                    });
                }}
              >
                <X size={18} />
              </button>
            </div>
          ))
        ) : (
          <div className="inline-empty">
            Δεν έχουν προστεθεί ακόμη ερωτήσεις. Οι τελικές ερωτήσεις μπορούν να οριστούν εδώ.
          </div>
        )}
        <form
          className="question-form"
          onSubmit={(event) => {
            event.preventDefault();
            const form = event.currentTarget;
            const data = new FormData(form);
            void action(async () => {
              await api(
                '/admin/questions',
                json({
                  title: data.get('title'),
                  selectionMode: data.get('selectionMode'),
                  sectionKey: data.get('sectionKey'),
                  required: data.get('required') === 'on',
                  options: String(data.get('options'))
                    .split('\n')
                    .map((option) => option.trim())
                    .filter(Boolean),
                }),
              );
              form.reset();
              setSuccess('Η ερώτηση προστέθηκε στο ερωτηματολόγιο.');
            });
          }}
        >
          <h3>Νέα ερώτηση</h3>
          <label>
            Τίτλος ερώτησης
            <input
              name="title"
              maxLength={256}
              required
              placeholder="Γράψε την ερώτηση που θα βλέπει ο πωλητής"
            />
          </label>
          <label>
            Τύπος απάντησης
            <select name="selectionMode" defaultValue="Single">
              <option value="Single">Μία επιλογή</option>
              <option value="Multiple">Πολλαπλές επιλογές</option>
            </select>
          </label>
          <label>
            Ενότητα
            <select name="sectionKey" defaultValue="">
              <option value="">Ανεξάρτητη πρόσθετη ερώτηση</option>
              {Array.from(
                new Map(
                  survey?.questions
                    .filter((question) => question.sectionKey)
                    .map((question) => [question.sectionKey, question.sectionTitle]),
                ).entries(),
              ).map(([key, title]) => (
                <option key={key} value={key}>
                  {title}
                </option>
              ))}
            </select>
          </label>
          <label>
            Επιλογές — μία ανά γραμμή
            <textarea
              name="options"
              required
              rows={4}
              placeholder={'Πρώτη επιλογή\nΔεύτερη επιλογή\nΤρίτη επιλογή'}
            />
          </label>
          <label className="inline-checkbox">
            <input name="required" type="checkbox" />
            Υποχρεωτική απάντηση (μόνο για ανεξάρτητη πρόσθετη ερώτηση)
          </label>
          <button className="btn secondary" disabled={busy}>
            <Plus size={17} />
            Προσθήκη ερώτησης
          </button>
        </form>
      </section>
    </>
  );
}

function App() {
  const [user, setUser] = useState<User | null>(null);
  const [setup, setSetup] = useState(false);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState('');
  const [overview, setOverview] = useState<Overview | null>(null);
  const route = useRoute();
  useEffect(() => {
    Promise.all([
      api<{ setupRequired: boolean }>('/auth/status').catch(error=>{if(session())return {setupRequired:false};throw error;}),
      api<User>('/auth/me').catch((error) => {
        if (error instanceof ApiError && error.status === 401) return null;
        throw error;
      }),
    ])
      .then(([status, user]) => {
        setSetup(status.setupRequired);
        setUser(user);
        if(user)setSession(user);
      })
      .catch((error) => setError(message(error)))
      .finally(() => setReady(true));
    const expired = () => {
      setUser(null);
      resetCsrf();
    };
    window.addEventListener('session-expired', expired);
    return () => window.removeEventListener('session-expired', expired);
  }, []);
  const refresh = () => {
    void api<Overview>('/overview')
      .then(setOverview)
      .catch((error) => setError(message(error)));
  };
  useEffect(() => {
    if (user) refresh();
  }, [user]);
  function go(path: string) {
    window.location.hash = path;
    window.scrollTo({ top: 0 });
  }
  async function logout() {
    if(user&&(await allOutbox(user.id)).length&&!window.confirm('Υπάρχουν επισκέψεις που δεν στάλθηκαν. Θα παραμείνουν στη συσκευή και θα σταλούν όταν συνδεθείς ξανά στον ίδιο λογαριασμό. Να αποσυνδεθείς;'))return;
    try {
      await api('/auth/logout', { method: 'POST' });
      setSession(null);
      setUser(null);
      resetCsrf();
      sessionStorage.removeItem('last-tax');
      window.location.hash = '/customers';
    } catch (error) {
      setError(message(error));
    }
  }
  if (!ready) return <Spinner />;
  if (!user)
    return error ? (
      <div className="connection-error">
        <Brand />
        <Notice>{error}</Notice>
        <button className="btn primary" onClick={() => window.location.reload()}>
          Δοκίμασε ξανά
        </button>
      </div>
    ) : (
      <Login
        setup={setup}
        onLogin={(user) => {
          setSetup(false);
          setUser(user);
        }}
      />
    );
  const customerRoute = route.startsWith('/customers/');
  let code = '';
  try {
    code = customerRoute ? decodeURIComponent(route.slice('/customers/'.length)) : '';
  } catch {
    /* Invalid routes display the not-found state. */
  }
  const nav = [
    { path: '/customers', label: 'Πελάτες', icon: Building2 },
    {
      path: '/visits',
      label: user.role === 'Admin' ? 'Επισκέψεις ομάδας' : 'Οι επισκέψεις μου',
      icon: ClipboardCheck,
    },
    ...(user.role === 'Admin' ? [{ path: '/settings', label: 'Ρυθμίσεις', icon: Settings2 }] : []),
  ];
  return (
    <div className="app">
      <a
        href="#main-content"
        className="skip-link"
        onClick={(event) => {
          event.preventDefault();
          document.getElementById('main-content')?.focus();
        }}
      >
        Μετάβαση στο περιεχόμενο
      </a>
      <aside className="sidebar">
        <Brand />
        <div className="workspace-label">ΧΩΡΟΣ ΕΡΓΑΣΙΑΣ</div>
        <nav aria-label="Κύρια πλοήγηση">
          {nav.map((item) => (
            <button
              key={item.path}
              onClick={() => go(item.path)}
              aria-current={route.startsWith(item.path) ? 'page' : undefined}
              className={route.startsWith(item.path) ? 'active' : ''}
            >
              <item.icon size={19} />
              {item.label}
              {route.startsWith(item.path) && <span className="nav-dot" />}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="sidebar-note">
            <span className="status-dot" />
            <span>Μαζί, στην επόμενη επίσκεψη.</span>
          </div>
          <div className="user-block">
            <span className="user-avatar">{user.name.slice(0, 1).toUpperCase()}</span>
            <div>
              <strong>{user.name}</strong>
              <small>{user.role === 'Admin' ? 'Διαχειριστής' : 'Πωλητής'}</small>
            </div>
            <button className="icon-button" aria-label="Αποσύνδεση" onClick={() => void logout()}>
              <LogOut size={18} />
            </button>
          </div>
        </div>
      </aside>
      <div className="main-shell">
        <header className="topbar">
          <span>
            Διαχείριση πελατών <ChevronRight size={13} />
            <b>
              {route === '/visits' ? 'Επισκέψεις' : route === '/settings' ? 'Ρυθμίσεις' : 'Πελάτες'}
            </b>
          </span>
          <span className="topbar-date">
            {new Intl.DateTimeFormat('el-GR', {
              day: 'numeric',
              month: 'long',
              year: 'numeric',
              timeZone: 'Europe/Athens',
            }).format(new Date())}
          </span>
        </header>
        <OfflineStatus key={user.id} user={user}/>
        <main id="main-content" tabIndex={-1}>
          {error && <Notice>{error}</Notice>}
          {route === '/customers' ? (
            <CustomerSearch go={go} overview={overview} />
          ) : customerRoute && code ? (
            <CustomerPage key={code} code={code} go={go} user={user} />
          ) : route.startsWith('/edit/') ? (<EditVisitPage key={route} id={route.slice('/edit/'.length)} user={user} go={go}/>) : route === '/visits' ? (
            <Visits user={user} go={go} />
          ) : route === '/settings' && user.role === 'Admin' ? (
            <Settings user={user} refresh={refresh} />
          ) : (
            <Empty title="Η σελίδα δεν βρέθηκε">
              <button className="text-button" onClick={() => go('/customers')}>
                Πίσω στους πελάτες
              </button>
            </Empty>
          )}
        </main>
        <footer className="app-footer">
          <span>
            paper. <span>Λιγότερη αναζήτηση. Καλύτερη εικόνα.</span>
          </span>
          <span>Πελάτες & επισκέψεις</span>
        </footer>
      </div>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
