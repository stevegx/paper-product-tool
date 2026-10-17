import { Check } from 'lucide-react';
import type { Visit } from './types';

export default function VisitAnswers({ visit }: { visit: Visit }) {
  const answered = visit.answers.filter(answer => answer.selectedOptions.length > 0);
  const groups = Array.from(new Set(answered.map(answer => answer.sectionKey || 'custom')))
    .map(key => ({
      key,
      title: answered.find(answer => (answer.sectionKey || 'custom') === key)?.sectionTitle || '',
      answers: answered.filter(answer => (answer.sectionKey || 'custom') === key),
    }));

  return <div className="visit-answers">
    {!answered.length && !visit.notes &&
      <p className="muted">Η επίσκεψη καταγράφηκε χωρίς απαντήσεις ή σημειώσεις.</p>}
    {groups.map(group => <div className="saved-section" key={group.key}>
      {group.title && <h4>{group.title}</h4>}
      {group.answers.map(answer => <div className="saved-answer" key={answer.questionId}>
        <strong>{answer.title}</strong>
        <div className="tags">{answer.selectedOptions.map(option =>
          <span className="tag" key={option}><Check size={12}/>{option}</span>)}</div>
      </div>)}
    </div>)}
    {visit.notes && <div className="saved-notes"><strong>Σημειώσεις</strong>
      <p className="preserve-text">{visit.notes}</p></div>}
  </div>;
}
