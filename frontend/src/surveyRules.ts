import type {Question} from './types';
type SurveyAnswers = Record<string,string[]>;
export function selectedAnswerIds(question:Question,answers:SurveyAnswers){
  return [...new Set(answers[question.id]??[])].filter(id=>question.options.some(option=>option.id===id));
}
export function hasAnsweredQuestions(questions:Question[],answers:SurveyAnswers){
  return questions.some(question=>selectedAnswerIds(question,answers).length>0);
}
export function hasVisitContent(questions:Question[],answers:SurveyAnswers,notes:string){
  return hasAnsweredQuestions(questions,answers)||notes.trim().length>0;
}
export function incompleteQuestions(questions: Question[], answers: SurveyAnswers) {
  const started = new Set(
    questions.filter((q) => q.sectionKey && selectedAnswerIds(q,answers).length).map((q) => q.sectionKey),
  );
  return questions.filter(
    (q) => !selectedAnswerIds(q,answers).length && (q.required || (q.sectionKey && started.has(q.sectionKey))),
  );
}
