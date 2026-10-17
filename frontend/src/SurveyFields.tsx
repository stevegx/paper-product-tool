import { CheckCircle2, Circle, RotateCcw } from 'lucide-react';
import {useState} from 'react';
import type { Question } from './types';
import './survey.css';
import {selectedAnswerIds} from './surveyRules';

export type SurveyAnswers = Record<string, string[]>;
export {incompleteQuestions} from './surveyRules';
export default function SurveyFields({
  questions,
  answers,
  onChange,
  activeSection,
  onSectionChange,
}: {
  questions: Question[];
  answers: SurveyAnswers;
  onChange: (answers: SurveyAnswers) => void;
  activeSection?: string;
  onSectionChange?: (key:string) => void;
}) {
  const [localSection,setLocalSection]=useState<string>();
  const groups = Array.from(new Set(questions.map((q) => q.sectionKey || 'custom'))).map((key) => ({
    key,
    title:
      questions.find((q) => (q.sectionKey || 'custom') === key)?.sectionTitle ||
      'Πρόσθετες ερωτήσεις',
    questions: questions.filter((q) => (q.sectionKey || 'custom') === key),
  }));
  const answered = questions.filter((q) => selectedAnswerIds(q,answers).length).length;
  const selectedKey=groups.some(group=>group.key===(activeSection??localSection))?(activeSection??localSection):groups[0]?.key;
  const select=(key:string)=>{setLocalSection(key);onSectionChange?.(key);};
  return (
    <div className="survey-fields">
      <div className="survey-instructions">
        <strong>
          {answered} / {questions.length} ερωτήσεις απαντημένες
        </strong>
        <p>
          Μπορείς να αφήσεις μια ολόκληρη ενότητα κενή. Αν απαντήσεις μία ερώτησή της, πρέπει να
          απαντήσεις όλες τις ερωτήσεις της ίδιας ενότητας.
        </p>
      </div>
      <nav className="survey-index" role="tablist" aria-label="Ενότητες ερωτηματολογίου">
        {groups.map((group, index) => (
          <button
            type="button"
            key={group.key}
            id={`tab-${group.key}`}
            role="tab"
            aria-selected={selectedKey===group.key}
            aria-controls={`section-${group.key}`}
            tabIndex={selectedKey===group.key?0:-1}
            onClick={()=>select(group.key)}
            onKeyDown={event=>{const step=event.key==='ArrowRight'?1:event.key==='ArrowLeft'?-1:0;if(step||event.key==='Home'||event.key==='End'){event.preventDefault();const next=event.key==='Home'?0:event.key==='End'?groups.length-1:(index+step+groups.length)%groups.length;select(groups[next].key);document.getElementById(`tab-${groups[next].key}`)?.focus();}}}
          >
            {index + 1}. {group.title}
            <small>{group.questions.filter(q=>selectedAnswerIds(q,answers).length).length}/{group.questions.length}</small>
          </button>
        ))}
      </nav>
      {groups.map((group, index) => {
        if(group.key!==selectedKey)return null;
        const count = group.questions.filter((q) => selectedAnswerIds(q,answers).length).length;
        const complete = count === group.questions.length;
        const partial = count > 0 && !complete && group.key !== 'custom';
        return (
          <section
            key={group.key}
            className={`survey-section ${complete ? 'completed' : partial ? 'started' : ''}`}
            id={`section-${group.key}`}
            role="tabpanel"
            aria-labelledby={`tab-${group.key}`}
          >
            <div className="survey-section-heading">
              <div>
                <span className="small-label">ΕΝΟΤΗΤΑ {String(index + 1).padStart(2, '0')}</span>
                <h3>{group.title}</h3>
              </div>
              <span className={`pill ${complete ? '' : 'subtle'}`}>
                {complete ? <CheckCircle2 size={14} /> : <Circle size={13} />} {count} /{' '}
                {group.questions.length}
              </span>
            </div>
            {partial && (
              <p className="section-help">
                Ξεκίνησες αυτή την ενότητα. Εκκρεμείς ερωτήσεις: {group.questions.length - count}.
              </p>
            )}
            {group.questions.map((question, questionIndex) => (
              <fieldset className="question" key={question.id}>
                <legend id={`survey-question-${question.id}`} tabIndex={-1}>
                  <span>{String(questionIndex + 1).padStart(2, '0')}</span>
                  {question.title}
                  <small className="choice-hint">
                    {question.selectionMode === 'Single' ? 'Μία επιλογή' : 'Πολλαπλές επιλογές'}
                  </small>
                  {question.required && <small>Υποχρεωτικό</small>}
                </legend>
                <div className="option-grid">
                  {question.options.map((option) => {
                    const selected = selectedAnswerIds(question,answers).includes(option.id);
                    return (
                      <label
                        className={`checkbox-option ${selected ? 'selected' : ''}`}
                        key={option.id}
                      >
                        <input
                          type={question.selectionMode === 'Single' ? 'radio' : 'checkbox'}
                          name={`question-${question.id}`}
                          checked={selected}
                          onClick={() => {
                            if(question.selectionMode==='Single' && selected)
                              onChange({...answers,[question.id]:[]});
                          }}
                          onChange={() =>
                            onChange({
                              ...answers,
                              [question.id]:
                                question.selectionMode === 'Single'
                                  ? [option.id]
                                  : selected
                                    ? (answers[question.id] ?? []).filter((id) => id !== option.id)
                                    : [...(answers[question.id] ?? []), option.id],
                            })
                          }
                        />
                        <span>{option.label}</span>
                      </label>
                    );
                  })}
                </div>
              </fieldset>
            ))}
            {count > 0 && (
              <button
                type="button"
                className="clear-section"
                onClick={() => {
                  const next = { ...answers };
                  group.questions.forEach((q) => delete next[q.id]);
                  onChange(next);
                }}
              >
                <RotateCcw size={14} />
                Καθαρισμός ενότητας
              </button>
            )}
          </section>
        );
      })}
    </div>
  );
}
