export type User = {
  id: string;
  name: string;
  email: string;
  role: 'Admin' | 'Seller';
  active: boolean;
};
export type Branch = { code: string; taxId: string; name: string; assignedSeller: string };
export type Question = {
  id: string;
  title: string;
  required: boolean;
  sectionKey: string;
  sectionTitle: string;
  selectionMode: 'Single' | 'Multiple';
  options: { id: string; label: string }[];
};
export type Survey = { version: string; questions: Question[] };
export type Visit = {
  id: string;
  branchCode: string;
  customerName: string;
  taxId: string;
  sellerName: string;
  userId: string;
  version: number;
  updatedAtUtc?: string | null;
  createdAtUtc: string;
  notes: string;
  answers: {
    questionId: string;
    title: string;
    selectedOptions: string[];
    sectionKey?: string;
    sectionTitle?: string;
    selectedOptionIds?: string[];
  }[];
};
export type Detail = {
  branch: Branch;
  year: number;
  total: number;
  categories: { id: number; name: string; amount: number }[];
  history: Visit[];
};
export type Overview = {
  customers: number;
  latestImport: { year: number; createdAtUtc: string } | null;
};
export type VisitPage = {
  total: number;
  customerCount: number;
  sellerCount: number;
  page: number;
  pageSize: number;
  items: Visit[];
};
