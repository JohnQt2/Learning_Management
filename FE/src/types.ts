export type Role = 'student' | 'lecturer' | 'admin';

export interface User {
  id: string;
  name: string;
  email: string;
  role: Role;
}

export interface Course {
  id: string;
  code: string;
  name: string;
  lecturer: string;
  semester: string;
}

export interface Assignment {
  id: string;
  courseId: string;
  title: string;
  dueDate: string;
  status: 'open' | 'submitted' | 'graded';
  score?: number;
}

export interface Submission {
  id: string;
  student: string;
  assignment: string;
  submittedAt: string;
  score?: number;
}

export interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
}

export interface LoginResult {
  token: string;
  user: User;
}
