import { api } from './api';
import { mockAssignments, mockCourses, mockSubmissions, mockUsers } from './mockData';
import type { Assignment, ChatMessage, Course, LoginResult, Role, Submission, User } from '../types';

// Mặc định dùng dữ liệu mẫu. Đặt VITE_USE_MOCK=false để gọi API thật.
const USE_MOCK = import.meta.env.VITE_USE_MOCK !== 'false';

function delay<T>(data: T, ms = 400): Promise<T> {
  return new Promise((resolve) => setTimeout(() => resolve(data), ms));
}

export async function login(email: string, password: string): Promise<LoginResult> {
  if (USE_MOCK) {
    // Quy ước demo: email chứa "admin" -> quản trị, chứa "gv" -> giảng viên, còn lại -> sinh viên
    const role: Role = email.includes('admin') ? 'admin' : email.includes('gv') ? 'lecturer' : 'student';
    return delay({ token: 'mock-token', user: { id: 'me', name: email.split('@')[0], email, role } });
  }
  const { data } = await api.post<LoginResult>('/auth/login', { email, password });
  return data;
}

export async function getCourses(): Promise<Course[]> {
  if (USE_MOCK) return delay(mockCourses);
  const { data } = await api.get<Course[]>('/courses');
  return data;
}

export async function getCourse(id: string): Promise<Course | undefined> {
  if (USE_MOCK) return delay(mockCourses.find((c) => c.id === id));
  const { data } = await api.get<Course>(`/courses/${id}`);
  return data;
}

export async function getAssignments(courseId: string): Promise<Assignment[]> {
  if (USE_MOCK) return delay(mockAssignments.filter((a) => a.courseId === courseId));
  const { data } = await api.get<Assignment[]>(`/courses/${courseId}/assignments`);
  return data;
}

export async function getSubmissions(): Promise<Submission[]> {
  if (USE_MOCK) return delay(mockSubmissions);
  const { data } = await api.get<Submission[]>('/submissions');
  return data;
}

export async function gradeSubmission(id: string, score: number): Promise<void> {
  if (USE_MOCK) {
    const s = mockSubmissions.find((x) => x.id === id);
    if (s) s.score = score;
    return delay(undefined);
  }
  await api.patch(`/submissions/${id}/grade`, { score });
}

export async function getUsers(): Promise<User[]> {
  if (USE_MOCK) return delay(mockUsers);
  const { data } = await api.get<User[]>('/admin/users');
  return data;
}

export async function askAi(history: ChatMessage[]): Promise<string> {
  if (USE_MOCK) {
    const last = history[history.length - 1]?.content ?? '';
    return delay(`Đây là câu trả lời mẫu cho: "${last}". Khi có backend, nội dung này sẽ do mô hình AI tạo ra.`, 900);
  }
  const { data } = await api.post<{ reply: string }>('/ai/chat', { messages: history });
  return data.reply;
}
