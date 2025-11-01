# SurveyWeb

Ứng dụng khảo sát (ASP.NET Core MVC, .NET 9) có phân khu vực Admin/Public. Repo này được cấu hình theo chuẩn quản lý dự án với nhánh, workflow CI, template issue/PR, và phân vai rõ ràng.

## Nhóm & Vai Trò
- Project Manager: Nguyễn Trọng Khôi
- Backend Developer: Nguyễn Trường An
- Frontend Developer: Nguyễn Trà Duy Đạt
- Tester/Reporter: Lê Ngọc Vũ
- Data Analyst: Dương Văn Huy

Vui lòng cập nhật GitHub username tương ứng trong `.github/CODEOWNERS` sau khi tạo repo trên GitHub.

## Nhánh & Quy ước
- Nhánh chính: `main`
- Nhánh phát triển: `develop`
- Nhánh tính năng: `feature/<mo-ta-ngan>`
- Nhánh sửa lỗi: `bugfix/<mo-ta-ngan>`
- Nhánh nóng: `hotfix/<mo-ta-ngan>`
- Nhánh phát hành: `release/<version>`

Commit theo Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`, `build:`, `ci:`.

## Chạy dự án (local)
- Yêu cầu: .NET SDK 9.0
- Lệnh:
  - Khôi phục: `dotnet restore`
  - Build: `dotnet build`
  - Chạy: `dotnet run --project SurveyWeb/SurveyWeb.csproj`

## CI
- Workflow GitHub Actions tự động build/test cho .NET 9 trên `push`/`pull_request` vào `main`/`develop`. Xem `.github/workflows/dotnet.yml`.

## Đóng góp
- Quy trình, tiêu chí review, và đặt tên nhánh ở `CONTRIBUTING.md`.
- Mẫu Issue/PR trong `.github/ISSUE_TEMPLATE` và `.github/PULL_REQUEST_TEMPLATE.md`.

## Bảo mật & Báo lỗi
- Xem `SECURITY.md` cho kênh báo cáo bảo mật.
- Báo lỗi sử dụng Issue template `Bug report`.

## Giấy phép
Vui lòng chọn giấy phép cho dự án (MIT/Apache-2.0/GPL-3.0, v.v.). Tạm thời để trống. Nếu muốn mình thêm `LICENSE`, hãy cho biết loại.

