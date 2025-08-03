"""
Setup script for ZeroBuffer Python implementation
"""

from setuptools import setup, find_packages

with open("README.md", "r", encoding="utf-8") as fh:
    long_description = fh.read()

setup(
    name="zerobuffer",
    version="0.1.0",
    author="ZeroBuffer Contributors",
    description="High-performance zero-copy inter-process communication",
    long_description=long_description,
    long_description_content_type="text/markdown",
    url="https://github.com/zerobuffer/zerobuffer-python",
    packages=find_packages(),
    classifiers=[
        "Development Status :: 4 - Beta",
        "Intended Audience :: Developers",
        "License :: OSI Approved :: MIT License",
        "Operating System :: OS Independent",
        "Programming Language :: Python :: 3",
        "Programming Language :: Python :: 3.8",
        "Programming Language :: Python :: 3.9",
        "Programming Language :: Python :: 3.10",
        "Programming Language :: Python :: 3.11",
        "Programming Language :: Python :: 3.12",
        "Topic :: System :: Hardware :: Symmetric Multi-processing",
        "Topic :: Software Development :: Libraries :: Python Modules",
    ],
    python_requires=">=3.8",
    install_requires=[
        # Platform-specific dependencies are handled as extras
    ],
    extras_require={
        "linux": ["posix-ipc>=1.0.0"],
        "darwin": ["posix-ipc>=1.0.0"],
        "windows": ["pywin32>=300"],
        "dev": [
            "pytest>=7.0",
            "pytest-timeout>=2.0",
            "pytest-cov>=4.0",
            "numpy>=1.20",  # For testing zero-copy with numpy arrays
        ],
    },
)