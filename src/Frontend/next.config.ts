import type { NextConfig } from "next";

const backendRaw = process.env.BACKEND_URL || "http://localhost:5101";
const backendOrigin = backendRaw.replace(/\/+$/, "").replace(/\/api$/i, "");

const nextConfig: NextConfig = {
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${backendOrigin}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
