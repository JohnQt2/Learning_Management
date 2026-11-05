import type { Assignment, Course, Submission, User } from '../types';

export const mockCourses: Course[] = [
  { id: 'c1', code: 'IT3040', name: 'Kỹ thuật lập trình', lecturer: 'TS. Nguyễn Văn An', semester: 'HK1 2026' },
  { id: 'c2', code: 'IT4409', name: 'Công nghệ Web', lecturer: 'ThS. Trần Thị Bình', semester: 'HK1 2026' },
  { id: 'c3', code: 'IT4770', name: 'Học máy', lecturer: 'PGS. Lê Minh Cường', semester: 'HK1 2026' },
];

export const mockAssignments: Assignment[] = [
  { id: 'a1', courseId: 'c1', title: 'Bài tập 1: Con trỏ và mảng', dueDate: '2026-11-12', status: 'graded', score: 8.5 },
  { id: 'a2', courseId: 'c1', title: 'Bài tập 2: Danh sách liên kết', dueDate: '2026-11-20', status: 'submitted' },
  { id: 'a3', courseId: 'c2', title: 'Lab 1: Giao diện với React', dueDate: '2026-11-15', status: 'open' },
  { id: 'a4', courseId: 'c3', title: 'Bài tập: Hồi quy tuyến tính', dueDate: '2026-11-25', status: 'open' },
];

export const mockSubmissions: Submission[] = [
  { id: 's1', student: 'Phạm Quốc Dũng', assignment: 'Bài tập 2: Danh sách liên kết', submittedAt: '2026-11-04 21:30' },
  { id: 's2', student: 'Hoàng Thu Hà', assignment: 'Bài tập 2: Danh sách liên kết', submittedAt: '2026-11-04 22:10', score: 9 },
  { id: 's3', student: 'Vũ Đức Khoa', assignment: 'Lab 1: Giao diện với React', submittedAt: '2026-11-05 08:45' },
];

export const mockUsers: User[] = [
  { id: 'u1', name: 'Phạm Quốc Dũng', email: 'dung@school.edu.vn', role: 'student' },
  { id: 'u2', name: 'TS. Nguyễn Văn An', email: 'gv.an@school.edu.vn', role: 'lecturer' },
  { id: 'u3', name: 'Quản trị hệ thống', email: 'admin@school.edu.vn', role: 'admin' },
];
