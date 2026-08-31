# Multi-stage Dockerfile for Kuddus HRM API (ASP.NET Core 9 + OpenCV YuNet/SFace)

# ---------------------------------------------------------
# Stage 1: Base Runtime with OpenCV native libraries
# ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

# Install native dependencies required by OpenCvSharp4 on Debian 12 / Linux
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgdiplus \
    libgl1 \
    libglib2.0-0 \
    libgomp1 \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
EXPOSE 8080

# ---------------------------------------------------------
# Stage 2: Build Application
# ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY AgenticHrmApi/AgenticHrmApi.csproj AgenticHrmApi/
RUN dotnet restore AgenticHrmApi/AgenticHrmApi.csproj

# Copy all source files
COPY AgenticHrmApi/ AgenticHrmApi/
WORKDIR /src/AgenticHrmApi
RUN dotnet build -c Release -o /app/build

# ---------------------------------------------------------
# Stage 3: Publish Binary
# ---------------------------------------------------------
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Ensure ONNX face models are present (download if not in repo)
RUN mkdir -p /app/publish/Models/onnx
RUN if [ ! -f /app/publish/Models/onnx/face_detection_yunet_2023mar.onnx ]; then \
        curl -sSL -o /app/publish/Models/onnx/face_detection_yunet_2023mar.onnx \
        https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx; \
    fi
RUN if [ ! -f /app/publish/Models/onnx/face_recognition_sface_2021dec.onnx ]; then \
        curl -sSL -o /app/publish/Models/onnx/face_recognition_sface_2021dec.onnx \
        https://github.com/opencv/opencv_zoo/raw/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx; \
    fi

# ---------------------------------------------------------
# Stage 4: Final Image
# ---------------------------------------------------------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "AgenticHrmApi.dll"]
